using Cinnabar.Client;

namespace Cinnabar.IntegrationTests;

/// <summary>The client's list editing, rename and float increment methods against a real server.</summary>
public sealed class ClientListAndKeyTests : IAsyncLifetime
{
    private TestServer _server = null!;

    private RedisClient Client => _server.Client;

    public async Task InitializeAsync() => _server = await TestServer.StartAsync();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task ListEditing()
    {
        (await Client.RPushIfExistsAsync("l", "x")).Should().Be(0);
        await Client.RPushAsync("l", "a", "b", "c", "b");
        (await Client.LPushIfExistsAsync("l", "z")).Should().Be(5);

        (await Client.LIndexAsync("l", -1)).Should().Be("b");
        await Client.LSetAsync("l", 0, "Z");
        (await Client.LInsertAsync("l", before: false, "a", "a2")).Should().Be(6);
        (await Client.LPosAsync("l", "b")).Should().Be(3);
        (await Client.LPosAsync("l", "b", rank: -1)).Should().Be(5);
        (await Client.LPosAsync("l", "nope")).Should().BeNull();
        (await Client.LRemAsync("l", 0, "b")).Should().Be(2);
        await Client.LTrimAsync("l", 1, -1);

        (await Client.LRangeAsync("l")).Should().Equal("a", "a2", "c");

        Func<Task> outOfRange = () => Client.LSetAsync("l", 99, "x");
        await outOfRange.Should().ThrowAsync<RedisException>().WithMessage("ERR index out of range");
    }

    [Fact]
    public async Task LMove_And_BLMove()
    {
        await Client.RPushAsync("src", "a", "b");

        (await Client.LMoveAsync("src", "dst", fromLeft: true, toLeft: false)).Should().Be("a");
        (await Client.BLMoveAsync("src", "dst", fromLeft: true, toLeft: false, TimeSpan.FromSeconds(1))).Should().Be("b");
        (await Client.BLMoveAsync("src", "dst", fromLeft: true, toLeft: false, TimeSpan.FromMilliseconds(50))).Should().BeNull();

        (await Client.LRangeAsync("dst")).Should().Equal("a", "b");
    }

    [Fact]
    public async Task BLMove_WakesWhenSourceIsPushed()
    {
        await using RedisClient other = _server.CreateClient();
        Task<string?> moved = other.BLMoveAsync("src", "dst", fromLeft: false, toLeft: true, timeout: null);
        await _server.UntilWatchedKeysAsync(1);

        await Client.RPushAsync("src", "job");

        (await moved.WithTimeout()).Should().Be("job");
        (await Client.LRangeAsync("dst")).Should().Equal("job");
    }

    [Fact]
    public async Task Rename_Unlink_And_IncrementByFloat()
    {
        await Client.SetAsync("a", "1", TimeSpan.FromMinutes(5));

        await Client.RenameAsync("a", "b");
        (await Client.GetAsync("b")).Should().Be("1");
        (await Client.TimeToLiveAsync("b")).Should().BeGreaterThan(TimeSpan.FromMinutes(4));

        await Client.SetAsync("c", "x");
        (await Client.RenameIfNotExistsAsync("b", "c")).Should().BeFalse();
        (await Client.RenameIfNotExistsAsync("b", "d")).Should().BeTrue();

        (await Client.IncrementByFloatAsync("d", 0.5)).Should().Be(1.5);
        (await Client.UnlinkAsync("c", "d", "missing")).Should().Be(2);

        Func<Task> missing = () => Client.RenameAsync("missing", "x");
        await missing.Should().ThrowAsync<RedisException>().WithMessage("ERR no such key");
    }
}
