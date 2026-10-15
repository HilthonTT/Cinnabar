using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Commands;

public sealed class ListCommandTests : DispatcherTestBase
{
    [Fact]
    public async Task Rpush_appends_and_returns_length()
    {
        (await Run("RPUSH", "l", "a", "b")).Should().Be(":2\r\n");
        (await Run("RPUSH", "l", "c")).Should().Be(":3\r\n");
        (await Run("LRANGE", "l", "0", "-1")).Should().Be(BulkArray("a", "b", "c"));
    }

    [Fact]
    public async Task Lpush_prepends_each_element_in_turn()
    {
        (await Run("LPUSH", "l", "a", "b", "c")).Should().Be(":3\r\n");
        (await Run("LRANGE", "l", "0", "-1")).Should().Be(BulkArray("c", "b", "a"));
    }

    [Fact]
    public async Task Push_onto_string_is_wrongtype()
    {
        await Run("SET", "k", "v");

        (await Run("LPUSH", "k", "a")).Should().StartWith("-WRONGTYPE");
    }

    [Fact]
    public async Task Lpop_and_rpop_take_from_each_end()
    {
        await Run("RPUSH", "l", "a", "b", "c");

        (await Run("LPOP", "l")).Should().Be(Bulk("a"));
        (await Run("RPOP", "l")).Should().Be(Bulk("c"));
        (await Run("LLEN", "l")).Should().Be(":1\r\n");
    }

    [Theory]
    [InlineData("LPOP")]
    [InlineData("RPOP")]
    public async Task Pop_missing_key_without_count_is_null_bulk_string(string command)
    {
        (await Run(command, "missing")).Should().Be("$-1\r\n");
    }

    [Theory]
    [InlineData("LPOP")]
    [InlineData("RPOP")]
    public async Task Pop_missing_key_with_count_is_null_array(string command)
    {
        (await Run(command, "missing", "2")).Should().Be("*-1\r\n");
    }

    [Fact]
    public async Task Lpop_with_count_returns_array_in_pop_order()
    {
        await Run("RPUSH", "l", "a", "b", "c");

        (await Run("LPOP", "l", "2")).Should().Be(BulkArray("a", "b"));
    }

    [Fact]
    public async Task Rpop_with_count_larger_than_list_returns_everything_and_deletes_key()
    {
        await Run("RPUSH", "l", "a", "b");

        (await Run("RPOP", "l", "10")).Should().Be(BulkArray("b", "a"));
        (await Run("EXISTS", "l")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Pop_with_negative_count_is_an_error()
    {
        await Run("RPUSH", "l", "a");

        (await Run("LPOP", "l", "-1")).Should().Be("-ERR value is out of range, must be positive\r\n");
    }

    [Fact]
    public async Task Pop_with_count_on_string_is_wrongtype()
    {
        await Run("SET", "k", "v");

        (await Run("LPOP", "k", "1")).Should().StartWith("-WRONGTYPE");
    }

    [Fact]
    public async Task Popping_last_element_deletes_key()
    {
        await Run("RPUSH", "l", "a");

        await Run("LPOP", "l");

        (await Run("TYPE", "l")).Should().Be("+none\r\n");
    }

    [Fact]
    public async Task Llen_of_missing_key_is_zero()
    {
        (await Run("LLEN", "missing")).Should().Be(":0\r\n");
    }

    [Fact]
    public async Task Llen_of_string_is_wrongtype()
    {
        await Run("SET", "k", "v");

        (await Run("LLEN", "k")).Should().StartWith("-WRONGTYPE");
    }

    [Theory]
    [InlineData("0", "0", new[] { "a" })]
    [InlineData("1", "2", new[] { "b", "c" })]
    [InlineData("-2", "-1", new[] { "d", "e" })]
    [InlineData("0", "100", new[] { "a", "b", "c", "d", "e" })]
    [InlineData("-100", "1", new[] { "a", "b" })]
    [InlineData("3", "1", new string[0])]
    [InlineData("10", "20", new string[0])]
    public async Task Lrange_handles_indexes(string start, string stop, string[] expected)
    {
        await Run("RPUSH", "l", "a", "b", "c", "d", "e");

        (await Run("LRANGE", "l", start, stop)).Should().Be(BulkArray(expected));
    }

    [Fact]
    public async Task Lrange_of_missing_key_is_empty_array()
    {
        (await Run("LRANGE", "missing", "0", "-1")).Should().Be("*0\r\n");
    }

    [Fact]
    public async Task Lrange_with_non_integer_index_is_an_error()
    {
        (await Run("LRANGE", "l", "a", "1")).Should().Be("-ERR value is not an integer or out of range\r\n");
    }
}
