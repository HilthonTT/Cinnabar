using Cinnabar.Server.Commands;
using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

public sealed class RenameCommandTests : DispatcherTestBase
{
    [Fact]
    public async Task Rename_moves_the_key()
    {
        await Run("SET", "a", "v");

        (await Run("RENAME", "a", "b")).Should().Be("+OK\r\n");

        (await Run("GET", "b")).Should().Be(Bulk("v"));
        (await Run("EXISTS", "a")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Rename_missing_key_is_an_error()
    {
        (await Run("RENAME", "missing", "b")).Should().Be("-ERR no such key\r\n");
    }

    [Fact]
    public async Task Renamenx_returns_whether_it_renamed()
    {
        await Run("SET", "a", "1");
        await Run("SET", "b", "2");

        (await Run("RENAMENX", "a", "b")).Should().Be(":0\r\n");
        (await Run("RENAMENX", "a", "c")).Should().Be(":1\r\n");
    }

    [Fact]
    public async Task Unlink_deletes_like_del()
    {
        await Run("SET", "a", "1");
        await Run("SET", "b", "2");

        (await Run("UNLINK", "a", "b", "c")).Should().Be(":2\r\n");
    }

    [Fact]
    public async Task Rename_wakes_a_client_blocked_on_the_new_name()
    {
        await Run("RPUSH", "tmp", "job");
        Task<Reply> blocked = H.Dispatch(H.NewSession(), "BLPOP", "queue", "0");
        while (H.Get<Cinnabar.Server.Blocking.KeyNotifier>().WatchedKeyCount == 0)
        {
            await Task.Delay(5);
        }

        await Run("RENAME", "tmp", "queue");

        (await blocked.WaitAsync(TimeSpan.FromSeconds(10))).ToString().Should().Be(BulkArray("queue", "job"));
    }

    [Fact]
    public async Task Incrbyfloat_returns_the_new_value()
    {
        (await Run("INCRBYFLOAT", "n", "10.5")).Should().Be(Bulk("10.5"));
        (await Run("INCRBYFLOAT", "n", "0.1")).Should().Be(Bulk("10.6"));
        (await Run("INCRBYFLOAT", "n", "-5e0")).Should().Be(Bulk("5.6"));
    }

    [Fact]
    public async Task Incrbyfloat_rejects_bad_increments()
    {
        (await Run("INCRBYFLOAT", "n", "abc")).Should().Be("-ERR value is not a valid float\r\n");
        (await Run("INCRBYFLOAT", "n", "inf")).Should().Be("-ERR increment would produce NaN or Infinity\r\n");
    }

    [Fact]
    public async Task Incrbyfloat_is_replicated_as_set_keepttl()
    {
        Reply reply = await H.Dispatch("INCRBYFLOAT", "n", "1.5");

        reply.PropagateAs!.ToString().Should().Be("SET n 1.5 KEEPTTL");
    }
}
