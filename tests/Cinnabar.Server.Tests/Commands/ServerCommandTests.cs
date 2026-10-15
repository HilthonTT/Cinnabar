using Cinnabar.Server.Commands;
using Cinnabar.Server.Hosting;
using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

public sealed class ServerCommandTests : DispatcherTestBase
{
    [Fact]
    public async Task Config_get_exact_name_returns_pair()
    {
        (await Run("CONFIG", "GET", "dbfilename")).Should().Be(BulkArray("dbfilename", "dump.rdb"));
    }

    [Fact]
    public async Task Config_get_dir_returns_full_path()
    {
        (await Run("CONFIG", "GET", "dir")).Should().Be(BulkArray("dir", Path.GetFullPath(H.Settings.Persistence.Directory)));
    }

    [Fact]
    public async Task Config_get_is_case_insensitive_for_patterns()
    {
        (await Run("config", "get", "DATABASES")).Should().Be(BulkArray("databases", "1"));
    }

    [Fact]
    public async Task Config_get_star_returns_every_parameter()
    {
        Reply reply = await H.Dispatch("CONFIG", "GET", "*");

        string text = reply.ToString();
        text.Should().StartWith("*22\r\n");
        foreach (string name in new[] { "bind", "port", "dir", "dbfilename", "requirepass", "masterauth", "replicaof", "maxclients", "save", "appendonly", "databases" })
        {
            text.Should().Contain(Bulk(name));
        }
    }

    [Fact]
    public async Task Config_get_glob_matches_several()
    {
        (await Run("CONFIG", "GET", "d*")).Should().Be(
            BulkArray("dir", Path.GetFullPath(H.Settings.Persistence.Directory), "dbfilename", "dump.rdb", "databases", "1"));
    }

    [Fact]
    public async Task Config_get_multiple_patterns()
    {
        (await Run("CONFIG", "GET", "appendonly", "databases")).Should().Be(
            BulkArray("appendonly", "no", "databases", "1"));
    }

    [Fact]
    public async Task Config_get_unknown_returns_empty_array()
    {
        (await Run("CONFIG", "GET", "nonsense")).Should().Be("*0\r\n");
    }

    [Fact]
    public async Task Config_get_without_pattern_is_an_arity_error()
    {
        (await Run("CONFIG", "GET")).Should().Be("-ERR wrong number of arguments for 'config|get' command\r\n");
    }

    [Fact]
    public async Task Config_set_is_not_supported()
    {
        (await Run("CONFIG", "SET", "port", "1")).Should().Be("-ERR CONFIG SET is not supported; only CONFIG GET is.\r\n");
    }

    [Fact]
    public async Task Info_returns_all_sections_by_default()
    {
        string info = await Run("INFO");

        foreach (string section in new[] { "# Server", "# Clients", "# Persistence", "# Replication", "# Keyspace" })
        {
            info.Should().Contain(section);
        }
    }

    [Fact]
    public async Task Info_section_returns_only_that_section()
    {
        string info = await Run("INFO", "Replication");

        info.Should().Contain("# Replication").And.NotContain("# Server").And.NotContain("# Keyspace");
    }

    [Fact]
    public async Task Info_on_master_reports_master_role_and_offset()
    {
        await Run("SET", "k", "v");

        string info = await Run("INFO", "replication");

        info.Should().Contain("role:master\r\n")
            .And.Contain("connected_slaves:0\r\n")
            .And.Contain($"master_repl_offset:{H.Master.Offset}\r\n")
            .And.Contain($"master_replid:{H.Master.ReplicationId}\r\n");
    }

    [Fact]
    public async Task Info_keyspace_counts_keys()
    {
        await Run("SET", "a", "1");
        await Run("SET", "b", "1");

        (await Run("INFO", "keyspace")).Should().Contain("db0:keys=2,");
    }

    [Fact]
    public async Task Info_unknown_section_is_empty_bulk_string()
    {
        (await Run("INFO", "nonsense")).Should().Be("$0\r\n\r\n");
    }

    [Fact]
    public async Task Command_count_matches_registered_handlers()
    {
        int expected = ServiceCollectionExtensions.DiscoverHandlers().Count();

        (await Run("COMMAND", "COUNT")).Should().Be($":{expected}\r\n");
    }

    [Fact]
    public async Task Command_lists_lower_case_names()
    {
        string reply = await Run("COMMAND");

        reply.Should().StartWith($"*{ServiceCollectionExtensions.DiscoverHandlers().Count()}\r\n");
        reply.Should().Contain(Bulk("get")).And.Contain(Bulk("blpop")).And.Contain(Bulk("xread"));
    }

    [Fact]
    public async Task Command_docs_returns_name_and_empty_map_pairs()
    {
        string reply = await Run("COMMAND", "DOCS");

        int count = ServiceCollectionExtensions.DiscoverHandlers().Count();
        reply.Should().StartWith($"*{count * 2}\r\n").And.Contain(Bulk("get") + "*0\r\n");
    }

    [Fact]
    public async Task Lastsave_is_zero_before_any_save()
    {
        (await Run("LASTSAVE")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Save_updates_lastsave()
    {
        await Run("SET", "k", "v");

        (await Run("SAVE")).Should().Be("+OK\r\n");

        (await Run("LASTSAVE")).Should().Be($":{H.Time.GetUtcNow().ToUnixTimeSeconds()}\r\n");
    }
}

public sealed class ReplicaServerCommandTests : DispatcherTestBase
{
    public ReplicaServerCommandTests()
        : base(s =>
        {
            s.Replication.MasterHost = "10.0.0.5";
            s.Replication.MasterPort = 7000;
        })
    {
    }

    [Fact]
    public async Task Info_on_replica_reports_slave_role()
    {
        string info = await Run("INFO", "replication");

        info.Should().Contain("role:slave\r\n")
            .And.Contain("master_host:10.0.0.5\r\n")
            .And.Contain("master_port:7000\r\n")
            .And.Contain("master_link_status:down\r\n")
            .And.NotContain("role:master");
    }

    [Fact]
    public async Task Config_get_replicaof_shows_master()
    {
        (await Run("CONFIG", "GET", "replicaof")).Should().Be(BulkArray("replicaof", "10.0.0.5 7000"));
    }

    [Fact]
    public async Task Wait_is_rejected_on_replica()
    {
        (await Run("WAIT", "0", "0")).Should().Be("-ERR WAIT cannot be used with replica instances.\r\n");
    }

    [Fact]
    public async Task Psync_is_rejected_on_replica()
    {
        (await Run("PSYNC", "?", "-1")).Should().StartWith("-ERR Chained replication is not supported");
    }
}
