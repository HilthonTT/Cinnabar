namespace Cinnabar.Protocol.Tests;

public class RespValueTests
{
    [Fact]
    public void AsString_returns_payload_of_string_types()
    {
        RespValue.SimpleString("ok").AsString().Should().Be("ok");
        RespValue.BulkString("bulk").AsString().Should().Be("bulk");
        RespValue.Error("ERR x").AsString().Should().Be("ERR x");
    }

    [Theory]
    [InlineData(0L, "0")]
    [InlineData(-15L, "-15")]
    [InlineData(long.MaxValue, "9223372036854775807")]
    public void AsString_formats_integers(long value, string expected)
    {
        RespValue.Integer(value).AsString().Should().Be(expected);
    }

    [Fact]
    public void AsString_is_null_for_nulls_and_arrays()
    {
        RespValue.Null().AsString().Should().BeNull();
        RespValue.NullArray().AsString().Should().BeNull();
        RespValue.Array([RespValue.Integer(1)]).AsString().Should().BeNull();
    }

    [Fact]
    public void AsLong_returns_integer()
    {
        RespValue.Integer(99).AsLong().Should().Be(99);
    }

    [Fact]
    public void AsLong_throws_for_non_integers()
    {
        // Even a numeric bulk string is not an integer reply.
        RespValue[] values = [RespValue.BulkString("5"), RespValue.SimpleString("5"), RespValue.Null(), RespValue.Array([])];

        foreach (RespValue value in values)
        {
            Action act = () => value.AsLong();
            act.Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void AsArray_returns_elements()
    {
        RespValue value = RespValue.Array([RespValue.Integer(1), RespValue.Integer(2)]);

        value.AsArray().Select(v => v.AsLong()).Should().Equal(1, 2);
    }

    [Fact]
    public void AsArray_of_null_array_is_empty()
    {
        RespValue.NullArray().AsArray().Should().BeEmpty();
    }

    [Fact]
    public void AsArray_throws_for_non_arrays()
    {
        RespValue[] values = [RespValue.BulkString("x"), RespValue.Null(), RespValue.Integer(1), RespValue.Error("ERR")];

        foreach (RespValue value in values)
        {
            Action act = () => value.AsArray();
            act.Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void AsStringList_converts_each_element()
    {
        RespValue value = RespValue.Array([RespValue.BulkString("a"), RespValue.Integer(2), RespValue.Null()]);

        value.AsStringList().Should().Equal("a", "2", null);
    }

    [Fact]
    public void AsStringList_of_null_array_is_empty_list()
    {
        RespValue.NullArray().AsStringList().Should().BeEmpty();
    }

    [Fact]
    public void AsStringList_throws_for_non_arrays()
    {
        Action act = () => RespValue.BulkString("x").AsStringList();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Null_flags()
    {
        RespValue.Null().IsNull.Should().BeTrue();
        RespValue.NullArray().IsNull.Should().BeTrue();
        RespValue.BulkString("").IsNull.Should().BeFalse();
        RespValue.Array([]).IsNull.Should().BeFalse();
    }

    [Fact]
    public void Null_instances_are_shared()
    {
        RespValue.Null().Should().BeSameAs(RespValue.Null());
        RespValue.NullArray().Should().BeSameAs(RespValue.NullArray());
    }

    [Fact]
    public void Error_flag()
    {
        RespValue.Error("ERR").IsError.Should().BeTrue();
        RespValue.SimpleString("ERR").IsError.Should().BeFalse();
    }

    [Fact]
    public void Non_integer_has_zero_integer_value_and_non_string_has_null_text()
    {
        RespValue.BulkString("5").IntegerValue.Should().Be(0);
        RespValue.Integer(5).Text.Should().BeNull();
        RespValue.Integer(5).Elements.Should().BeNull();
    }

    [Fact]
    public void ToString_is_human_readable()
    {
        RespValue.Null().ToString().Should().Be("(nil)");
        RespValue.NullArray().ToString().Should().Be("(nil array)");
        RespValue.Array([RespValue.SimpleString("a"), RespValue.BulkString("b")]).ToString().Should().Be("[+a, \"b\"]");
    }
}
