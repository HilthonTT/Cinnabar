using Cinnabar.Server.Configuration;

namespace Cinnabar.Server.Tests.Configuration;

public sealed class CommandLineTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "cinnabar-cli-tests", Guid.NewGuid().ToString("N"));

    public CommandLineTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    private static ServerSettings Parse(params string[] args) => CommandLine.Parse(args, out _);

    private string WriteConfig(string json)
    {
        string path = Path.Combine(_directory, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void No_arguments_gives_defaults()
    {
        ServerSettings settings = CommandLine.Parse([], out bool help);

        help.Should().BeFalse();
        settings.Network.BindAddress.Should().Be("0.0.0.0");
        settings.Network.Port.Should().Be(6379);
        settings.Persistence.Directory.Should().Be("data");
        settings.Persistence.DbFileName.Should().Be("dump.rdb");
        settings.Persistence.SaveIntervalSeconds.Should().Be(0);
        settings.Replication.IsReplica.Should().BeFalse();
        settings.Security.RequiresAuthentication.Should().BeFalse();
        settings.Diagnostics.MetricsPort.Should().Be(9090);
        settings.Diagnostics.LogLevel.Should().Be("Information");
    }

    [Fact]
    public void Every_flag_is_applied()
    {
        ServerSettings settings = Parse(
            "--bind", "127.0.0.1",
            "--port", "7000",
            "--requirepass", "pw",
            "--dir", "/tmp/x",
            "--dbfilename", "snap.rdb",
            "--save-interval", "60",
            "--masterauth", "mpw",
            "--metrics-port", "9100",
            "--loglevel", "Debug");

        settings.Network.BindAddress.Should().Be("127.0.0.1");
        settings.Network.Port.Should().Be(7000);
        settings.Security.RequirePass.Should().Be("pw");
        settings.Persistence.Directory.Should().Be("/tmp/x");
        settings.Persistence.DbFileName.Should().Be("snap.rdb");
        settings.Persistence.SaveIntervalSeconds.Should().Be(60);
        settings.Replication.MasterAuth.Should().Be("mpw");
        settings.Diagnostics.MetricsPort.Should().Be(9100);
        settings.Diagnostics.LogLevel.Should().Be("Debug");
    }

    [Fact]
    public void Flags_are_case_insensitive()
    {
        Parse("--PORT", "1234").Network.Port.Should().Be(1234);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65535")]
    public void Port_accepts_boundaries(string port)
    {
        Parse("--port", port).Network.Port.Should().Be(int.Parse(port));
    }

    [Fact]
    public void Metrics_port_zero_disables_metrics()
    {
        Parse("--metrics-port", "0").Diagnostics.MetricsPort.Should().Be(0);
    }

    [Theory]
    [InlineData("--port", "65536")]
    [InlineData("--port", "-1")]
    [InlineData("--port", "abc")]
    [InlineData("--metrics-port", "70000")]
    [InlineData("--save-interval", "-1")]
    [InlineData("--save-interval", "1.5")]
    public void Invalid_numbers_throw(string flag, string value)
    {
        Action act = () => Parse(flag, value);

        act.Should().Throw<ArgumentException>().WithMessage($"*{flag}*");
    }

    [Theory]
    [InlineData("--port")]
    [InlineData("--bind")]
    [InlineData("--requirepass")]
    [InlineData("--config")]
    [InlineData("--replicaof")]
    public void Missing_value_throws(string flag)
    {
        Action act = () => Parse(flag);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Unknown_flag_throws()
    {
        Action act = () => Parse("--nope");

        act.Should().Throw<ArgumentException>().WithMessage("*Unknown option '--nope'*");
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    public void Help_flag_sets_show_help(string flag)
    {
        CommandLine.Parse([flag], out bool help);

        help.Should().BeTrue();
    }

    [Fact]
    public void Usage_mentions_every_flag()
    {
        foreach (string flag in new[] { "--config", "--bind", "--port", "--requirepass", "--dir", "--dbfilename", "--save-interval", "--replicaof", "--masterauth", "--metrics-port", "--loglevel", "--help" })
        {
            CommandLine.Usage.Should().Contain(flag);
        }
    }

    // ───────────── --replicaof ─────────────

    [Fact]
    public void Replicaof_as_two_tokens()
    {
        ServerSettings settings = Parse("--replicaof", "master.local", "6380");

        settings.Replication.MasterHost.Should().Be("master.local");
        settings.Replication.MasterPort.Should().Be(6380);
        settings.Replication.IsReplica.Should().BeTrue();
    }

    [Fact]
    public void Replicaof_as_one_quoted_token()
    {
        ServerSettings settings = Parse("--replicaof", "master.local 6380");

        settings.Replication.MasterHost.Should().Be("master.local");
        settings.Replication.MasterPort.Should().Be(6380);
    }

    [Fact]
    public void Replicaof_as_host_colon_port()
    {
        ServerSettings settings = Parse("--replicaof", "10.0.0.1:6380");

        settings.Replication.MasterHost.Should().Be("10.0.0.1");
        settings.Replication.MasterPort.Should().Be(6380);
    }

    [Fact]
    public void Replicaof_followed_by_other_flags()
    {
        ServerSettings settings = Parse("--replicaof", "h", "1", "--port", "2");

        settings.Replication.MasterHost.Should().Be("h");
        settings.Replication.MasterPort.Should().Be(1);
        settings.Network.Port.Should().Be(2);
    }

    [Theory]
    [InlineData("no", "one")]
    [InlineData("NO", "ONE")]
    public void Replicaof_no_one_as_two_tokens_means_master(string first, string second)
    {
        ServerSettings settings = Parse("--replicaof", "h", "1", "--replicaof", first, second);

        settings.Replication.IsReplica.Should().BeFalse();
        settings.Replication.MasterHost.Should().BeNull();
    }

    [Fact]
    public void Replicaof_no_one_as_one_token_means_master()
    {
        Parse("--replicaof", "no one").Replication.IsReplica.Should().BeFalse();
    }

    [Theory]
    [InlineData("host", "0")]
    [InlineData("host", "65536")]
    [InlineData("host", "port")]
    public void Replicaof_invalid_port_throws(string host, string port)
    {
        Action act = () => Parse("--replicaof", host, port);

        act.Should().Throw<ArgumentException>().WithMessage("*--replicaof*");
    }

    [Theory]
    [InlineData("host:")]
    [InlineData("host:abc")]
    public void Replicaof_invalid_colon_form_throws(string value)
    {
        Action act = () => Parse("--replicaof", value);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Replicaof_host_without_port_throws()
    {
        Action act = () => Parse("--replicaof", "host");

        act.Should().Throw<ArgumentException>();
    }

    // ───────────── --config ─────────────

    [Fact]
    public void Config_file_values_are_loaded()
    {
        string path = WriteConfig("""
            {
              // comments are allowed
              "network": { "bindAddress": "127.0.0.2", "port": 7001 },
              "persistence": { "directory": "snapshots", "saveIntervalSeconds": 30 },
              "replication": { "masterHost": "m", "masterPort": 7002 },
              "security": { "requirePass": "filepw", "maxConnections": 5 },
              "diagnostics": { "metricsPort": 0 },
            }
            """);

        ServerSettings settings = Parse("--config", path);

        settings.Network.BindAddress.Should().Be("127.0.0.2");
        settings.Network.Port.Should().Be(7001);
        settings.Persistence.Directory.Should().Be("snapshots");
        settings.Persistence.SaveIntervalSeconds.Should().Be(30);
        settings.Replication.MasterHost.Should().Be("m");
        settings.Replication.MasterPort.Should().Be(7002);
        settings.Security.RequirePass.Should().Be("filepw");
        settings.Security.MaxConnections.Should().Be(5);
        settings.Diagnostics.MetricsPort.Should().Be(0);
    }

    [Fact]
    public void Flags_override_config_file_regardless_of_order()
    {
        string path = WriteConfig("""{ "network": { "port": 7001, "bindAddress": "127.0.0.2" } }""");

        ServerSettings settings = Parse("--port", "8000", "--config", path);

        settings.Network.Port.Should().Be(8000);
        settings.Network.BindAddress.Should().Be("127.0.0.2");
    }

    [Fact]
    public void Partial_config_keeps_defaults()
    {
        string path = WriteConfig("""{ "security": { "requirePass": "x" } }""");

        ServerSettings settings = Parse("--config", path);

        settings.Security.RequirePass.Should().Be("x");
        settings.Security.Username.Should().Be("default");
        settings.Network.Port.Should().Be(6379);
        settings.Persistence.DbFileName.Should().Be("dump.rdb");
        settings.Diagnostics.MetricsPort.Should().Be(9090);
    }

    [Fact]
    public void Empty_json_object_gives_defaults()
    {
        Parse("--config", WriteConfig("{}")).Network.Port.Should().Be(6379);
    }

    [Fact]
    public void Json_null_gives_defaults()
    {
        Parse("--config", WriteConfig("null")).Network.Port.Should().Be(6379);
    }

    [Fact]
    public void Invalid_json_throws_argument_exception()
    {
        string path = WriteConfig("{ not json");

        Action act = () => Parse("--config", path);

        act.Should().Throw<ArgumentException>().WithMessage("*not valid JSON*");
    }

    [Fact]
    public void Wrong_json_type_throws_argument_exception()
    {
        string path = WriteConfig("""{ "network": { "port": "abc" } }""");

        Action act = () => Parse("--config", path);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Missing_config_file_throws_argument_exception()
    {
        string path = Path.Combine(_directory, "missing.json");

        Action act = () => Parse("--config", path);

        act.Should().Throw<ArgumentException>().WithMessage("*does not exist*");
    }
}
