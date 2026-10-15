using Cinnabar.Protocol;

namespace Cinnabar.Client.Tests;

public class RedisExceptionTests
{
    [Theory]
    [InlineData("ERR value is not an integer or out of range", "ERR")]
    [InlineData("WRONGTYPE Operation against a key holding the wrong kind of value", "WRONGTYPE")]
    [InlineData("NOAUTH Authentication required.", "NOAUTH")]
    [InlineData("SINGLEWORD", "SINGLEWORD")]
    [InlineData("", "")]
    public void ErrorCode_is_first_word(string message, string code)
    {
        new RedisException(message).ErrorCode.Should().Be(code);
    }

    [Fact]
    public void Message_is_full_reply()
    {
        new RedisException("ERR syntax error").Message.Should().Be("ERR syntax error");
    }

    [Fact]
    public void ThrowIfError_throws_for_error_reply()
    {
        Action act = () => RespValue.Error("WRONGTYPE bad").ThrowIfError();

        RedisException exception = act.Should().Throw<RedisException>().Which;
        exception.Message.Should().Be("WRONGTYPE bad");
        exception.ErrorCode.Should().Be("WRONGTYPE");
    }

    [Fact]
    public void ThrowIfError_returns_same_value_otherwise()
    {
        RespValue[] values = [RespValue.SimpleString("OK"), RespValue.Integer(1), RespValue.Null(), RespValue.NullArray(), RespValue.Array([RespValue.Error("ERR nested")])];

        foreach (RespValue value in values)
        {
            value.ThrowIfError().Should().BeSameAs(value);
        }
    }

    [Fact]
    public void RedisConnectionException_keeps_inner_exception()
    {
        var inner = new IOException("boom");

        var exception = new RedisConnectionException("failed", inner);

        exception.Message.Should().Be("failed");
        exception.InnerException.Should().BeSameAs(inner);
    }
}
