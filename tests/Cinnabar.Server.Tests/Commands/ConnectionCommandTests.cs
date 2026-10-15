using Cinnabar.Server.Commands;
using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

public sealed class ConnectionCommandTests : DispatcherTestBase
{
    [Fact]
    public async Task Ping_replies_pong()
    {
        (await Run("PING")).Should().Be("+PONG\r\n");
    }

    [Fact]
    public async Task Ping_with_message_echoes_it_as_bulk_string()
    {
        (await Run("PING", "hello world")).Should().Be(Bulk("hello world"));
    }

    [Fact]
    public async Task Ping_with_two_arguments_is_an_arity_error()
    {
        (await Run("PING", "a", "b")).Should().Be("-ERR wrong number of arguments for 'ping' command\r\n");
    }

    [Fact]
    public async Task Echo_returns_bulk_string()
    {
        (await Run("ECHO", "hi")).Should().Be("$2\r\nhi\r\n");
    }

    [Fact]
    public async Task Echo_is_safe_for_newlines()
    {
        (await Run("ECHO", "a\r\nb")).Should().Be("$4\r\na\r\nb\r\n");
    }

    [Fact]
    public async Task Echo_counts_utf8_bytes()
    {
        (await Run("ECHO", "é")).Should().Be("$2\r\né\r\n");
    }

    [Fact]
    public async Task Quit_replies_ok_and_closes()
    {
        Reply reply = await H.Dispatch("QUIT");

        reply.ToString().Should().Be("+OK\r\n");
        reply.CloseConnection.Should().BeTrue();
    }

    [Fact]
    public async Task Select_zero_is_ok()
    {
        (await Run("SELECT", "0")).Should().Be("+OK\r\n");
    }

    [Fact]
    public async Task Select_other_database_is_out_of_range()
    {
        (await Run("SELECT", "1")).Should().Be("-ERR DB index is out of range\r\n");
    }

    [Fact]
    public async Task Select_non_integer_is_an_error()
    {
        (await Run("SELECT", "x")).Should().Be("-ERR value is not an integer or out of range\r\n");
    }

    [Fact]
    public async Task Client_id_returns_session_id()
    {
        (await Run("CLIENT", "ID")).Should().Be($":{H.Session.Id}\r\n");
    }

    [Fact]
    public void Client_ids_are_unique()
    {
        H.NewSession().Id.Should().NotBe(H.NewSession().Id);
    }

    [Fact]
    public async Task Client_getname_is_null_by_default()
    {
        (await Run("CLIENT", "GETNAME")).Should().Be("$-1\r\n");
    }

    [Fact]
    public async Task Client_setname_then_getname()
    {
        (await Run("client", "setname", "worker-1")).Should().Be("+OK\r\n");

        (await Run("CLIENT", "GETNAME")).Should().Be(Bulk("worker-1"));
    }

    [Fact]
    public async Task Client_setname_empty_clears_name()
    {
        await Run("CLIENT", "SETNAME", "x");

        await Run("CLIENT", "SETNAME", "");

        (await Run("CLIENT", "GETNAME")).Should().Be("$-1\r\n");
    }

    [Fact]
    public async Task Client_setname_with_space_is_rejected()
    {
        (await Run("CLIENT", "SETNAME", "a b")).Should().StartWith("-ERR Client names cannot contain spaces");
    }

    [Fact]
    public async Task Client_setinfo_is_accepted()
    {
        (await Run("CLIENT", "SETINFO", "lib-name", "cinnabar")).Should().Be("+OK\r\n");
    }

    [Theory]
    [InlineData("KILL")]
    [InlineData("ID", "extra")]
    public async Task Client_unknown_subcommand_is_an_error(params string[] args)
    {
        (await Run(["CLIENT", .. args])).Should().StartWith("-ERR unknown subcommand or wrong number of arguments");
    }
}
