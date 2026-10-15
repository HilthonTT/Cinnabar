using System.Globalization;
using Cinnabar.Client;
using Cinnabar.Protocol;

namespace Cinnabar.IntegrationTests;

/// <summary>Every <see cref="RedisClient"/> method against a real server over TCP.</summary>
public sealed class ClientApiTests : IAsyncLifetime
{
    private TestServer _server = null!;

    private RedisClient Client => _server.Client;

    public async Task InitializeAsync() => _server = await TestServer.StartAsync();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    // ─────────────────────────── Connection / server ───────────────────────────

    [Fact]
    public async Task Echo_ReturnsMessage()
    {
        (await Client.EchoAsync("hello there")).Should().Be("hello there");
    }

    [Fact]
    public async Task Info_ContainsServerAndReplicationSections()
    {
        string? info = await Client.InfoAsync();
        info.Should().Contain("# Server").And.Contain("role:master").And.Contain($"tcp_port:{_server.Port}");

        string? replication = await Client.InfoAsync("replication");
        replication.Should().Contain("# Replication").And.NotContain("# Server");
    }

    [Fact]
    public async Task DbSize_And_FlushDb()
    {
        await Client.SetAsync("a", "1");
        await Client.SetAsync("b", "2");
        (await Client.DbSizeAsync()).Should().Be(2);

        await Client.FlushDbAsync();

        (await Client.DbSizeAsync()).Should().Be(0);
        (await Client.GetAsync("a")).Should().BeNull();
    }

    [Fact]
    public async Task ConfigGet_ReturnsNameValuePairs()
    {
        (await Client.ConfigGetAsync("port")).Should().Equal("port", _server.Port.ToString(CultureInfo.InvariantCulture));
        (await Client.ConfigGetAsync("databases")).Should().Equal("databases", "1");
    }

    [Fact]
    public async Task ExecuteAsync_RunsRawCommand()
    {
        RespValue reply = await Client.ExecuteAsync("PING", "custom");
        reply.AsString().Should().Be("custom");

        RespValue ttl = await Client.ExecuteAsync("TTL", "missing");
        ttl.AsLong().Should().Be(-2);
    }

    // ─────────────────────────── Strings ───────────────────────────

    [Fact]
    public async Task Get_MissingKey_ReturnsNull()
    {
        (await Client.GetAsync("nope")).Should().BeNull();
    }

    [Fact]
    public async Task Set_OverwritesValue()
    {
        await Client.SetAsync("k", "one");
        await Client.SetAsync("k", "two");
        (await Client.GetAsync("k")).Should().Be("two");
    }

    [Fact]
    public async Task SetWithExpiry_SetsTimeToLive()
    {
        await Client.SetAsync("temp", "v", TimeSpan.FromMinutes(1));

        TimeSpan? ttl = await Client.TimeToLiveAsync("temp");
        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeGreaterThan(TimeSpan.FromSeconds(50)).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task SetWithShortExpiry_KeyDisappears()
    {
        await Client.SetAsync("short", "v", TimeSpan.FromMilliseconds(100));
        await Wait.UntilAsync(async () => await Client.GetAsync("short") is null, "the key expires");
        (await Client.ExistsAsync("short")).Should().Be(0);
    }

    [Fact]
    public async Task SetIfNotExists_OnlySetsOnce()
    {
        (await Client.SetIfNotExistsAsync("lock", "owner-1")).Should().BeTrue();
        (await Client.SetIfNotExistsAsync("lock", "owner-2")).Should().BeFalse();
        (await Client.GetAsync("lock")).Should().Be("owner-1");
    }

    [Fact]
    public async Task SetIfNotExists_WithExpiry_SetsTimeToLive()
    {
        (await Client.SetIfNotExistsAsync("lock", "me", TimeSpan.FromSeconds(30))).Should().BeTrue();
        (await Client.TimeToLiveAsync("lock")).Should().NotBeNull();
    }

    [Fact]
    public async Task Expire_TimeToLive_Persist()
    {
        await Client.SetAsync("k", "v");
        (await Client.TimeToLiveAsync("k")).Should().BeNull("a key without TTL has none");

        (await Client.ExpireAsync("k", TimeSpan.FromSeconds(100))).Should().BeTrue();
        (await Client.TimeToLiveAsync("k")).Should().BeGreaterThan(TimeSpan.FromSeconds(90));

        (await Client.PersistAsync("k")).Should().BeTrue();
        (await Client.TimeToLiveAsync("k")).Should().BeNull();
        (await Client.PersistAsync("k")).Should().BeFalse("there is no TTL left to remove");
    }

    [Fact]
    public async Task Expire_MissingKey_ReturnsFalse()
    {
        (await Client.ExpireAsync("missing", TimeSpan.FromSeconds(10))).Should().BeFalse();
        (await Client.TimeToLiveAsync("missing")).Should().BeNull();
    }

    [Fact]
    public async Task Increment_And_Decrement()
    {
        (await Client.IncrementAsync("counter")).Should().Be(1);
        (await Client.IncrementAsync("counter")).Should().Be(2);
        (await Client.IncrementByAsync("counter", 10)).Should().Be(12);
        (await Client.DecrementAsync("counter")).Should().Be(11);
        (await Client.DecrementByAsync("counter", 20)).Should().Be(-9);
        (await Client.GetAsync("counter")).Should().Be("-9");
    }

    [Fact]
    public async Task Increment_NonInteger_ThrowsRedisException()
    {
        await Client.SetAsync("text", "abc");
        Func<Task> act = () => Client.IncrementAsync("text");
        (await act.Should().ThrowAsync<RedisException>()).Which.ErrorCode.Should().Be("ERR");
    }

    // ─────────────────────────── Keys ───────────────────────────

    [Fact]
    public async Task Delete_And_Exists_WithSeveralKeys()
    {
        await Client.SetAsync("a", "1");
        await Client.SetAsync("b", "2");
        await Client.RPushAsync("c", "x");

        (await Client.ExistsAsync("a", "b", "c", "missing")).Should().Be(3);
        (await Client.ExistsAsync("a", "a")).Should().Be(2, "repeated keys count again");

        (await Client.DeleteAsync("a", "c", "missing")).Should().Be(2);
        (await Client.ExistsAsync("a", "b", "c")).Should().Be(1);
    }

    [Fact]
    public async Task Type_ReportsEachKind()
    {
        await Client.SetAsync("s", "v");
        await Client.RPushAsync("l", "v");
        await Client.XAddAsync("x", [new("f", "v")]);

        (await Client.TypeAsync("s")).Should().Be("string");
        (await Client.TypeAsync("l")).Should().Be("list");
        (await Client.TypeAsync("x")).Should().Be("stream");
        (await Client.TypeAsync("missing")).Should().Be("none");
    }

    [Fact]
    public async Task Keys_MatchesPattern()
    {
        await Client.SetAsync("user:1", "a");
        await Client.SetAsync("user:2", "b");
        await Client.SetAsync("order:1", "c");

        (await Client.KeysAsync("user:*")).Should().BeEquivalentTo(["user:1", "user:2"]);
        (await Client.KeysAsync()).Should().HaveCount(3);
        (await Client.KeysAsync("user:?")).Should().HaveCount(2);
    }

    // ─────────────────────────── Lists ───────────────────────────

    [Fact]
    public async Task Push_Range_Length()
    {
        (await Client.RPushAsync("l", "b", "c")).Should().Be(2);
        (await Client.LPushAsync("l", "a")).Should().Be(3);

        (await Client.LLenAsync("l")).Should().Be(3);
        (await Client.LRangeAsync("l")).Should().Equal("a", "b", "c");
        (await Client.LRangeAsync("l", 1, 1)).Should().Equal("b");
        (await Client.LRangeAsync("l", -2)).Should().Equal("b", "c");
        (await Client.LLenAsync("missing")).Should().Be(0);
        (await Client.LRangeAsync("missing")).Should().BeEmpty();
    }

    [Fact]
    public async Task LPush_MultipleValues_PushesEachToHead()
    {
        await Client.LPushAsync("l", "a", "b", "c");
        (await Client.LRangeAsync("l")).Should().Equal("c", "b", "a");
    }

    [Fact]
    public async Task LPop_And_RPop()
    {
        await Client.RPushAsync("l", "a", "b", "c");

        (await Client.LPopAsync("l")).Should().Be("a");
        (await Client.RPopAsync("l")).Should().Be("c");
        (await Client.LPopAsync("l")).Should().Be("b");
        (await Client.LPopAsync("l")).Should().BeNull();
        (await Client.RPopAsync("l")).Should().BeNull();
        (await Client.ExistsAsync("l")).Should().Be(0, "an emptied list is removed");
    }

    [Fact]
    public async Task LPopWithCount_ReturnsUpToCount()
    {
        await Client.RPushAsync("l", "a", "b", "c");

        (await Client.LPopAsync("l", 2)).Should().Equal("a", "b");
        (await Client.LPopAsync("l", 5)).Should().Equal("c");
    }

    [Fact]
    public async Task LPopWithCount_MissingKey_ReturnsEmptyList()
    {
        (await Client.LPopAsync("missing", 3)).Should().BeEmpty();
    }

    [Fact]
    public async Task BLPop_ByKey_ReturnsAvailableElement()
    {
        await Client.RPushAsync("q", "job");

        (string Key, string Value)? popped = await Client.BLPopAsync("q", 1);

        popped.Should().Be(("q", "job"));
    }

    [Fact]
    public async Task BLPop_SeveralKeys_PopsFromFirstNonEmpty()
    {
        await Client.RPushAsync("second", "x");

        (string Key, string Value)? popped = await Client.BLPopAsync(["first", "second"], TimeSpan.FromSeconds(1));

        popped.Should().Be(("second", "x"));
    }

    [Fact]
    public async Task BLPop_Timeout_ReturnsNull()
    {
        (await Client.BLPopAsync(["empty"], TimeSpan.FromMilliseconds(200))).Should().BeNull();
    }

    [Fact]
    public async Task BLPop_UnderDecimalCommaCulture_StillWorks()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-BE");
        try
        {
            (await Client.BLPopAsync("empty", 0.5)).Should().BeNull("the 0.5 s timeout must be sent as '0.5', not '0,5'");

            await Client.RPushAsync("q", "v");
            (await Client.BLPopAsync("q", 0.5)).Should().Be(("q", "v"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ─────────────────────────── Streams ───────────────────────────

    [Fact]
    public async Task XAdd_XLen_XRange()
    {
        string first = await Client.XAddAsync("s", [new("temp", "20"), new("unit", "C")]);
        string second = await Client.XAddAsync("s", [new("temp", "21")]);

        (await Client.XLenAsync("s")).Should().Be(2);

        List<RedisStreamEntry> entries = await Client.XRangeAsync("s");
        entries.Select(e => e.Id).Should().Equal(first, second);
        entries[0].Fields.Should().Equal(new KeyValuePair<string, string>("temp", "20"), new KeyValuePair<string, string>("unit", "C"));
    }

    [Fact]
    public async Task XAdd_ExplicitId_And_XRangeWithCount()
    {
        (await Client.XAddAsync("s", "1-1", [new("n", "1")])).Should().Be("1-1");
        (await Client.XAddAsync("s", "1-2", [new("n", "2")])).Should().Be("1-2");
        (await Client.XAddAsync("s", "2-0", [new("n", "3")])).Should().Be("2-0");

        (await Client.XRangeAsync("s", count: 2)).Select(e => e.Id).Should().Equal("1-1", "1-2");
        (await Client.XRangeAsync("s", "1-2", "+")).Select(e => e.Id).Should().Equal("1-2", "2-0");
        (await Client.XLenAsync("missing")).Should().Be(0);
    }

    [Fact]
    public async Task XAdd_IdNotGreaterThanTop_ThrowsRedisException()
    {
        await Client.XAddAsync("s", "5-0", [new("a", "b")]);
        Func<Task> act = () => Client.XAddAsync("s", "4-0", [new("a", "b")]);
        (await act.Should().ThrowAsync<RedisException>()).Which.ErrorCode.Should().Be("ERR");
    }

    // ─────────────────────────── Errors ───────────────────────────

    [Fact]
    public async Task WrongType_ThrowsRedisExceptionWithCode()
    {
        await Client.RPushAsync("list", "a");

        Func<Task> act = () => Client.GetAsync("list");

        (await act.Should().ThrowAsync<RedisException>()).Which.ErrorCode.Should().Be("WRONGTYPE");
    }

    [Fact]
    public async Task UnknownCommand_ThrowsRedisException()
    {
        Func<Task> act = () => Client.ExecuteAsync("NOSUCHCOMMAND", "x");

        RedisException error = (await act.Should().ThrowAsync<RedisException>()).Which;
        error.ErrorCode.Should().Be("ERR");
        error.Message.Should().Contain("unknown command");
    }

    [Fact]
    public async Task ErrorReply_LeavesClientUsable()
    {
        await Client.RPushAsync("list", "a");
        await FluentActions.Awaiting(() => Client.GetAsync("list")).Should().ThrowAsync<RedisException>();

        (await Client.PingAsync()).Should().BeTrue();
    }

    // ─────────────────────────── Transactions ───────────────────────────

    [Fact]
    public async Task ExecuteTransaction_ReturnsReplyPerCommand()
    {
        IReadOnlyList<RespValue> replies = await Client.ExecuteTransactionAsync(
        [
            ["SET", "k", "1"],
            ["INCR", "k"],
            ["GET", "k"],
        ]);

        replies.Should().HaveCount(3);
        replies[0].AsString().Should().Be("OK");
        replies[1].AsLong().Should().Be(2);
        replies[2].AsString().Should().Be("2");
    }

    [Fact]
    public async Task ExecuteTransaction_RuntimeError_IsReturnedNotThrown()
    {
        await Client.RPushAsync("list", "a");

        IReadOnlyList<RespValue> replies = await Client.ExecuteTransactionAsync(
        [
            ["INCR", "list"],
            ["SET", "after", "ok"],
        ]);

        replies[0].IsError.Should().BeTrue();
        replies[0].Text.Should().StartWith("WRONGTYPE");
        (await Client.GetAsync("after")).Should().Be("ok", "later commands still run");
    }

    [Fact]
    public async Task ExecuteTransaction_QueueingError_ThrowsExecAbort()
    {
        Func<Task> act = () => Client.ExecuteTransactionAsync(
        [
            ["SET", "k", "v"],
            ["NOSUCHCOMMAND"],
        ]);

        (await act.Should().ThrowAsync<RedisException>()).Which.ErrorCode.Should().Be("EXECABORT");
        (await Client.GetAsync("k")).Should().BeNull("nothing in an aborted transaction runs");
    }
}
