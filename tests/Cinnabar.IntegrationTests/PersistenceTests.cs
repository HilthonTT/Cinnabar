using Cinnabar.Client;
using Cinnabar.Server.Configuration;
using Cinnabar.Server.Hosting;
using Microsoft.Extensions.Logging;

namespace Cinnabar.IntegrationTests;

/// <summary>RDB snapshots: save on shutdown, load on startup, SAVE/BGSAVE, corrupt files.</summary>
public sealed class PersistenceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "cinnabar-tests", "persistence-" + Guid.NewGuid().ToString("N"));

    private string DumpPath => Path.Combine(_directory, "dump.rdb");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing was written.
        }
    }

    private Task<TestServer> StartAsync(bool saveOnShutdown = true) => TestServer.StartAsync(s =>
    {
        s.Persistence.Directory = _directory;
        s.Persistence.LoadOnStartup = true;
        s.Persistence.SaveOnShutdown = saveOnShutdown;
    });

    [Fact]
    public async Task Restart_RestoresStringsListsAndTtls()
    {
        await using (TestServer first = await StartAsync())
        {
            await first.Client.SetAsync("plain", "value ✓");
            await first.Client.SetAsync("expiring", "soon", TimeSpan.FromHours(1));
            await first.Client.RPushAsync("list", "a", "b", "c");
            await first.Client.IncrementByAsync("counter", 42);
            await first.Server.StopAsync();
        }

        File.Exists(DumpPath).Should().BeTrue("SaveOnShutdown writes a snapshot");

        await using TestServer second = await StartAsync();
        RedisClient client = second.Client;

        (await client.GetAsync("plain")).Should().Be("value ✓");
        (await client.GetAsync("expiring")).Should().Be("soon");
        (await client.TimeToLiveAsync("expiring")).Should().BeGreaterThan(TimeSpan.FromMinutes(59));
        (await client.TimeToLiveAsync("plain")).Should().BeNull();
        (await client.LRangeAsync("list")).Should().Equal("a", "b", "c");
        (await client.IncrementAsync("counter")).Should().Be(43);
        (await client.DbSizeAsync()).Should().Be(4);
    }

    [Fact]
    public async Task Restart_RestoresHashesSetsAndSortedSets()
    {
        await using (TestServer first = await StartAsync())
        {
            await first.Client.HSetAsync("hash", [new("name", "Zoë"), new("n", "1")]);
            await first.Client.SAddAsync("set", "a", "b");
            await first.Client.ZAddAsync("zset", [new("x", 1.25), new("y", double.NegativeInfinity)]);
            await first.Client.ExecuteAsync("EXPIRE", "set", "3600");
        }

        await using TestServer second = await StartAsync();
        RedisClient client = second.Client;

        (await client.HGetAllAsync("hash")).Should().BeEquivalentTo(new Dictionary<string, string> { ["name"] = "Zoë", ["n"] = "1" });
        (await client.SMembersAsync("set")).Should().BeEquivalentTo(["a", "b"]);
        (await client.TimeToLiveAsync("set")).Should().BeGreaterThan(TimeSpan.FromMinutes(59));
        (await client.ZRangeAsync("zset")).Should().Equal(
            new RedisSortedSetEntry("y", double.NegativeInfinity),
            new RedisSortedSetEntry("x", 1.25));
    }

    [Fact]
    public async Task Restart_ExpiredKeysAreNotRestored()
    {
        await using (TestServer first = await StartAsync())
        {
            await first.Client.SetAsync("short", "v", TimeSpan.FromMilliseconds(300));
            await first.Client.SetAsync("long", "v");
        }

        await Task.Delay(400); // let "short" expire while the server is down

        await using TestServer second = await StartAsync();
        (await second.Client.GetAsync("short")).Should().BeNull();
        (await second.Client.GetAsync("long")).Should().Be("v");
    }

    [Fact]
    public async Task WithoutSaveOnShutdown_NothingIsWritten()
    {
        await using (TestServer first = await StartAsync(saveOnShutdown: false))
        {
            await first.Client.SetAsync("k", "v");
        }

        File.Exists(DumpPath).Should().BeFalse();
    }

    [Fact]
    public async Task Save_WritesFileAndUpdatesLastSave()
    {
        await using TestServer server = await StartAsync(saveOnShutdown: false);
        (await server.Client.ExecuteAsync("LASTSAVE")).AsLong().Should().Be(0);
        await server.Client.SetAsync("k", "v");

        (await server.Client.ExecuteAsync("SAVE")).AsString().Should().Be("OK");

        File.Exists(DumpPath).Should().BeTrue();
        (await server.Client.ExecuteAsync("LASTSAVE")).AsLong()
            .Should().BeCloseTo(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 5);
        (await server.Client.InfoAsync("persistence")).Should().NotContain("rdb_last_save_time:0\r\n");
    }

    [Fact]
    public async Task Save_ThenRestartWithoutShutdownSave_RestoresSavedData()
    {
        await using (TestServer first = await StartAsync(saveOnShutdown: false))
        {
            await first.Client.SetAsync("saved", "yes");
            await first.Client.ExecuteAsync("SAVE");
            await first.Client.SetAsync("unsaved", "no");
        }

        await using TestServer second = await StartAsync(saveOnShutdown: false);
        (await second.Client.GetAsync("saved")).Should().Be("yes");
        (await second.Client.GetAsync("unsaved")).Should().BeNull();
    }

    [Fact]
    public async Task BgSave_WritesFileInBackground()
    {
        await using TestServer server = await StartAsync(saveOnShutdown: false);
        await server.Client.RPushAsync("list", "x", "y");

        (await server.Client.ExecuteAsync("BGSAVE")).AsString().Should().Be("Background saving started");

        await Wait.UntilAsync(
            async () => (await server.Client.ExecuteAsync("LASTSAVE")).AsLong() != 0,
            "the background save finishes");
        File.Exists(DumpPath).Should().BeTrue();
    }

    [Fact]
    public async Task CorruptSnapshot_StartupFailsAndFileIsKept()
    {
        Directory.CreateDirectory(_directory);
        byte[] garbage = "REDIS0011 this is definitely not a valid snapshot \x01\x02\x03"u8.ToArray();
        await File.WriteAllBytesAsync(DumpPath, garbage);

        ServerSettings settings = TestServer.CreateSettings();
        settings.Persistence.Directory = _directory;
        settings.Persistence.LoadOnStartup = true;
        settings.Persistence.SaveOnShutdown = true;
        CinnabarServer server = CinnabarServer.Create(settings, logging => logging.SetMinimumLevel(LogLevel.None));

        Func<Task> start = () => server.StartAsync();

        await start.Should().ThrowAsync<InvalidDataException>();
        await server.DisposeAsync();

        (await File.ReadAllBytesAsync(DumpPath)).Should().Equal(garbage, "a failed load must never be followed by overwriting the user's file");
    }

    [Fact]
    public async Task NotAnRdbFile_StartupFails()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(DumpPath, "hello, I am a text file");

        ServerSettings settings = TestServer.CreateSettings();
        settings.Persistence.Directory = _directory;
        settings.Persistence.LoadOnStartup = true;
        await using CinnabarServer server = CinnabarServer.Create(settings, logging => logging.SetMinimumLevel(LogLevel.None));

        await FluentActions.Awaiting(() => server.StartAsync()).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task ConfigGet_ReportsDirectoryAndFileName()
    {
        await using TestServer server = await StartAsync(saveOnShutdown: false);

        (await server.Client.ConfigGetAsync("dir")).Should().Equal("dir", Path.GetFullPath(_directory));
        (await server.Client.ConfigGetAsync("dbfilename")).Should().Equal("dbfilename", "dump.rdb");
    }
}
