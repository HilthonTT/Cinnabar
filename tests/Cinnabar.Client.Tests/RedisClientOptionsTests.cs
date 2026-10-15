namespace Cinnabar.Client.Tests;

public class RedisClientOptionsTests
{
    private static RedisClientOptions Parse(string connectionString) => new() { ConnectionString = connectionString };

    [Fact]
    public void Defaults()
    {
        var options = new RedisClientOptions();

        options.Host.Should().Be("localhost");
        options.Port.Should().Be(6379);
        options.Username.Should().Be("default");
        options.Password.Should().BeNull();
        options.PoolSize.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Host_and_port()
    {
        RedisClientOptions options = Parse("cache.example.com:6380");

        options.Host.Should().Be("cache.example.com");
        options.Port.Should().Be(6380);
        options.Password.Should().BeNull();
    }

    [Fact]
    public void Host_only_keeps_default_port()
    {
        RedisClientOptions options = Parse("cache.example.com");

        options.Host.Should().Be("cache.example.com");
        options.Port.Should().Be(6379);
    }

    [Fact]
    public void Password_at_host_port()
    {
        RedisClientOptions options = Parse("s3cret@cache:6380");

        options.Password.Should().Be("s3cret");
        options.Username.Should().Be("default");
        options.Host.Should().Be("cache");
        options.Port.Should().Be(6380);
    }

    [Fact]
    public void User_and_password_at_host_port()
    {
        RedisClientOptions options = Parse("alice:s3cret@cache:6380");

        options.Username.Should().Be("alice");
        options.Password.Should().Be("s3cret");
        options.Host.Should().Be("cache");
        options.Port.Should().Be(6380);
    }

    [Fact]
    public void Empty_username_means_default_user()
    {
        RedisClientOptions options = Parse(":s3cret@cache");

        options.Username.Should().Be("default");
        options.Password.Should().Be("s3cret");
        options.Host.Should().Be("cache");
        options.Port.Should().Be(6379);
    }

    [Theory]
    [InlineData("redis://cache:6380")]
    [InlineData("rediss://cache:6380")]
    [InlineData("  redis://cache:6380  ")]
    public void Scheme_is_ignored(string connectionString)
    {
        RedisClientOptions options = Parse(connectionString);

        options.Host.Should().Be("cache");
        options.Port.Should().Be(6380);
    }

    [Fact]
    public void Scheme_with_credentials()
    {
        RedisClientOptions options = Parse("redis://bob:pw@cache:7000");

        options.Username.Should().Be("bob");
        options.Password.Should().Be("pw");
        options.Host.Should().Be("cache");
        options.Port.Should().Be(7000);
    }

    [Fact]
    public void Password_may_contain_at_sign_and_colon()
    {
        RedisClientOptions options = Parse("redis://carol:p@ss:w0rd@cache:6390");

        options.Username.Should().Be("carol");
        options.Password.Should().Be("p@ss:w0rd");
        options.Host.Should().Be("cache");
        options.Port.Should().Be(6390);
    }

    [Theory]
    [InlineData("redis://cache:6380/")]
    [InlineData("cache:6380//")]
    public void Trailing_slash_is_ignored(string connectionString)
    {
        RedisClientOptions options = Parse(connectionString);

        options.Host.Should().Be("cache");
        options.Port.Should().Be(6380);
    }

    [Fact]
    public void Trailing_slash_after_host_only()
    {
        RedisClientOptions options = Parse("redis://cache/");

        options.Host.Should().Be("cache");
        options.Port.Should().Be(6379);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_connection_string_changes_nothing(string? connectionString)
    {
        var options = new RedisClientOptions { Host = "h", Port = 1234 };

        options.ConnectionString = connectionString;

        options.Host.Should().Be("h");
        options.Port.Should().Be(1234);
    }

    [Fact]
    public void Getter_without_password_is_host_and_port()
    {
        Parse("cache:6380").ConnectionString.Should().Be("cache:6380");
    }

    [Fact]
    public void Getter_masks_password()
    {
        RedisClientOptions options = Parse("alice:hunter2@cache:6380");

        options.ConnectionString.Should().Be("alice:***@cache:6380");
        options.ConnectionString.Should().NotContain("hunter2");
    }

    [Fact]
    public void Getter_masks_password_with_default_user()
    {
        RedisClientOptions options = Parse("hunter2@cache");

        options.ConnectionString.Should().Be("default:***@cache:6379");
        options.ConnectionString.Should().NotContain("hunter2");
    }

    [Fact]
    public void Getter_masks_password_set_directly()
    {
        var options = new RedisClientOptions { Host = "h", Port = 1, Password = "topsecret" };

        options.ConnectionString.Should().Be("default:***@h:1");
    }

    [Fact]
    public void Masked_getter_does_not_round_trip_the_password()
    {
        RedisClientOptions options = Parse("alice:hunter2@cache:6380");

        RedisClientOptions copy = Parse(options.ConnectionString!);

        copy.Password.Should().Be("***");
        copy.Host.Should().Be("cache");
    }

    [Fact]
    public void Valid_options_pass_validation()
    {
        Action act = () => new RedisClientOptions().Validate();

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_rejects_blank_host(string host)
    {
        Action act = () => new RedisClientOptions { Host = host }.Validate();

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("Host");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Validate_rejects_out_of_range_port(int port)
    {
        Action act = () => new RedisClientOptions { Port = port }.Validate();

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("Port");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public void Validate_accepts_port_boundaries(int port)
    {
        Action act = () => new RedisClientOptions { Port = port }.Validate();

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validate_rejects_non_positive_pool_size(int poolSize)
    {
        Action act = () => new RedisClientOptions { PoolSize = poolSize }.Validate();

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("PoolSize");
    }

    [Fact]
    public void Client_constructor_validates_options()
    {
        Action act = () => _ = new RedisClient(new RedisClientOptions { Port = 0 });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Setting_a_connection_string_without_credentials_clears_earlier_ones()
    {
        var options = new RedisClientOptions { ConnectionString = "alice:secret@first:6379" };

        options.ConnectionString = "second:6380";

        options.Password.Should().BeNull();
        options.Username.Should().Be("default");
        options.Host.Should().Be("second");
        options.Port.Should().Be(6380);
    }

    [Theory]
    [InlineData("redis://cache:6380/0")]
    [InlineData("redis://user:p%2Fw@cache:6380/0")]
    public void Database_path_is_not_part_of_the_host(string connectionString)
    {
        var options = new RedisClientOptions { ConnectionString = connectionString };

        options.Host.Should().Be("cache");
        options.Port.Should().Be(6380);
    }

    [Fact]
    public void Slash_in_password_is_kept()
    {
        var options = new RedisClientOptions { ConnectionString = "user:pa/ss@cache:6380/0" };

        options.Password.Should().Be("pa/ss");
        options.Host.Should().Be("cache");
    }
}
