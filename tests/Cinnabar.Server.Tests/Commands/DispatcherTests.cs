using Cinnabar.Server.Commands;
using Cinnabar.Server.Networking;
using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

public sealed class DispatcherTests : DispatcherTestBase
{
    [Fact]
    public async Task Unknown_command_returns_error_listing_its_arguments()
    {
        Reply reply = await H.Dispatch("foo", "a", "b");

        reply.IsError.Should().BeTrue();
        reply.ToString().Should().StartWith("-ERR unknown command 'foo', with args beginning with: 'a' 'b'");
    }

    [Fact]
    public async Task Unknown_command_lists_at_most_five_arguments()
    {
        string reply = await Run("foo", "1", "2", "3", "4", "5", "6");

        reply.Should().Contain("'5'").And.NotContain("'6'");
    }

    [Theory]
    [InlineData("ping")]
    [InlineData("PING")]
    [InlineData("PiNg")]
    public async Task Command_names_are_case_insensitive(string name)
    {
        (await Run(name)).Should().Be("+PONG\r\n");
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("GET", "a", "b")]
    public async Task Wrong_argument_count_is_rejected(params string[] parts)
    {
        (await Run(parts)).Should().Be("-ERR wrong number of arguments for 'get' command\r\n");
    }

    [Fact]
    public async Task Arity_error_uses_lower_case_name()
    {
        (await Run("SeT", "k")).Should().Be("-ERR wrong number of arguments for 'set' command\r\n");
    }

    [Fact]
    public async Task Storage_exception_becomes_error_reply()
    {
        await Run("RPUSH", "list", "a");

        Reply reply = await H.Dispatch("GET", "list");

        reply.IsError.Should().BeTrue();
        reply.ToString().Should().Be("-WRONGTYPE Operation against a key holding the wrong kind of value\r\n");
    }

    // ───────────── Subscribed mode ─────────────

    [Fact]
    public async Task Subscribed_mode_rejects_regular_commands()
    {
        await Run("SUBSCRIBE", "news");

        Reply reply = await H.Dispatch("GET", "k");

        reply.IsError.Should().BeTrue();
        reply.ToString().Should().StartWith("-ERR Can't execute 'get': only (P|S)SUBSCRIBE / (P|S)UNSUBSCRIBE / PING / QUIT");
    }

    [Fact]
    public async Task Subscribed_mode_allows_ping()
    {
        await Run("SUBSCRIBE", "news");

        (await H.Dispatch("PING")).IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Subscribed_mode_ends_after_last_unsubscribe()
    {
        await Run("SUBSCRIBE", "news");
        await Run("UNSUBSCRIBE", "news");

        (await Run("GET", "k")).Should().Be("$-1\r\n");
    }

    // ───────────── Propagation ─────────────

    [Fact]
    public async Task Successful_write_on_master_propagates()
    {
        long before = H.Master.Offset;

        await Run("SET", "k", "v");

        H.Master.Offset.Should().BeGreaterThan(before);
        H.Session.LastWriteOffset.Should().Be(H.Master.Offset);
    }

    [Fact]
    public async Task Propagated_offset_grows_by_encoded_command_length()
    {
        await Run("SET", "k", "v");
        long before = H.Master.Offset;

        // DEL of an existing key changes data, so it is propagated (a no-op DEL is not).
        await Run("DEL", "k");

        H.Master.Offset.Should().Be(before + new Command("DEL", "k").Encode().Length);
    }

    [Fact]
    public async Task Read_does_not_propagate()
    {
        await Run("GET", "k");

        H.Master.Offset.Should().Be(0);
        H.Session.LastWriteOffset.Should().Be(0);
    }

    [Fact]
    public async Task Failed_write_does_not_propagate()
    {
        await Run("SET", "k", "notanumber");
        long before = H.Master.Offset;

        (await H.Dispatch("INCR", "k")).IsError.Should().BeTrue();

        H.Master.Offset.Should().Be(before);
    }

    [Fact]
    public async Task Rejected_nx_set_does_not_propagate()
    {
        await Run("SET", "k", "v");
        long before = H.Master.Offset;

        (await Run("SET", "k", "w", "NX")).Should().Be("$-1\r\n");

        H.Master.Offset.Should().Be(before);
    }

    [Fact]
    public async Task Rejected_xx_set_with_expiry_does_not_propagate()
    {
        (await Run("SET", "missing", "w", "XX", "EX", "10")).Should().Be("$-1\r\n");

        H.Master.Offset.Should().Be(0);
    }

    [Fact]
    public async Task Rejected_nx_set_with_past_exat_does_not_propagate()
    {
        await Run("SET", "k", "v");
        long before = H.Master.Offset;

        (await Run("SET", "k", "w", "NX", "EXAT", "1")).Should().Be("$-1\r\n");

        H.Master.Offset.Should().Be(before, "a SET that changed nothing must not be sent to replicas");
    }
}

public sealed class AuthenticationTests : DispatcherTestBase
{
    public AuthenticationTests()
        : base(s => s.Security.RequirePass = "secret")
    {
    }

    [Fact]
    public async Task Commands_before_auth_get_noauth()
    {
        (await Run("GET", "k")).Should().Be("-NOAUTH Authentication required.\r\n");
    }

    [Fact]
    public async Task Ping_before_auth_gets_noauth()
    {
        (await Run("PING")).Should().Be("-NOAUTH Authentication required.\r\n");
    }

    [Fact]
    public async Task Quit_is_allowed_before_auth()
    {
        Reply reply = await H.Dispatch("QUIT");

        reply.ToString().Should().Be("+OK\r\n");
        reply.CloseConnection.Should().BeTrue();
    }

    [Fact]
    public async Task Correct_password_authenticates()
    {
        (await Run("AUTH", "secret")).Should().Be("+OK\r\n");

        H.Session.IsAuthenticated.Should().BeTrue();
        (await Run("GET", "k")).Should().Be("$-1\r\n");
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        (await Run("AUTH", "nope")).Should().Be("-WRONGPASS invalid username-password pair or user is disabled.\r\n");

        H.Session.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task Auth_with_default_username_succeeds()
    {
        (await Run("AUTH", "default", "secret")).Should().Be("+OK\r\n");
    }

    [Fact]
    public async Task Auth_with_wrong_username_is_rejected()
    {
        (await Run("AUTH", "admin", "secret")).Should().StartWith("-WRONGPASS");
        H.Session.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task Auth_with_too_many_arguments_is_an_arity_error()
    {
        (await Run("AUTH", "a", "b", "c")).Should().Be("-ERR wrong number of arguments for 'auth' command\r\n");
    }

    [Fact]
    public async Task Unknown_command_is_reported_before_auth_check()
    {
        (await Run("NOPE")).Should().StartWith("-ERR unknown command 'NOPE'");
    }
}

public sealed class AuthWithoutPasswordTests : DispatcherTestBase
{
    [Fact]
    public async Task Auth_without_configured_password_is_an_error()
    {
        (await Run("AUTH", "whatever")).Should().StartWith("-ERR AUTH <password> called without any password configured");
    }
}

public sealed class ReplicaDispatcherTests : DispatcherTestBase
{
    public ReplicaDispatcherTests()
        : base(s => s.Replication.MasterHost = "127.0.0.1")
    {
    }

    [Fact]
    public async Task Writes_from_clients_are_rejected_on_a_replica()
    {
        (await Run("SET", "k", "v")).Should().Be("-READONLY You can't write against a read only replica.\r\n");
    }

    [Fact]
    public async Task Reads_are_allowed_on_a_replica()
    {
        (await Run("GET", "k")).Should().Be("$-1\r\n");
    }

    [Fact]
    public async Task Replication_link_may_write_on_a_replica()
    {
        ClientSession link = ClientSession.ForReplicationLink();

        (await H.Dispatch(link, "SET", "k", "v")).ToString().Should().Be("+OK\r\n");
        (await Run("GET", "k")).Should().Be("$1\r\nv\r\n");
    }

    [Fact]
    public async Task Replica_does_not_propagate_writes()
    {
        ClientSession link = ClientSession.ForReplicationLink();

        await H.Dispatch(link, "SET", "k", "v");

        H.Master.Offset.Should().Be(0);
    }

    [Fact]
    public async Task Blocking_pop_from_client_is_rejected_on_a_replica()
    {
        (await Run("BLPOP", "k", "0.1")).Should().StartWith("-READONLY");
    }
}
