using System.IO.Pipelines;

namespace Cinnabar.Protocol.Tests;

public class RespReaderAsyncTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static async Task WriteAsync(Pipe pipe, string text)
    {
        await pipe.Writer.WriteAsync(Resp.Bytes(text));
    }

    [Fact]
    public async Task Returns_null_when_completed_with_empty_buffer()
    {
        var pipe = new Pipe();
        await pipe.Writer.CompleteAsync();

        RespValue? value = await RespReader.ReadAsync(pipe.Reader).AsTask().WaitAsync(Timeout);

        value.Should().BeNull();
    }

    [Fact]
    public async Task Returns_value_then_null_on_clean_completion()
    {
        var pipe = new Pipe();
        await WriteAsync(pipe, "+OK\r\n");
        await pipe.Writer.CompleteAsync();

        RespValue? first = await RespReader.ReadAsync(pipe.Reader).AsTask().WaitAsync(Timeout);
        RespValue? second = await RespReader.ReadAsync(pipe.Reader).AsTask().WaitAsync(Timeout);

        first!.Text.Should().Be("OK");
        second.Should().BeNull();
    }

    [Fact]
    public async Task Throws_end_of_stream_when_completed_mid_value()
    {
        var pipe = new Pipe();
        await WriteAsync(pipe, "$5\r\nhel");
        await pipe.Writer.CompleteAsync();

        Func<Task> act = () => RespReader.ReadAsync(pipe.Reader).AsTask().WaitAsync(Timeout);

        await act.Should().ThrowAsync<EndOfStreamException>();
    }

    [Fact]
    public async Task Reads_value_written_in_two_chunks()
    {
        var pipe = new Pipe();
        await WriteAsync(pipe, "$5\r\nhel");

        Task<RespValue?> read = RespReader.ReadAsync(pipe.Reader).AsTask();
        await Task.Delay(50);
        read.IsCompleted.Should().BeFalse("the value is incomplete");

        await WriteAsync(pipe, "lo\r\n");
        RespValue? value = await read.WaitAsync(Timeout);

        value!.Text.Should().Be("hello");
    }

    [Fact]
    public async Task Reads_pipelined_values_from_one_write()
    {
        var pipe = new Pipe();
        await WriteAsync(pipe, ":1\r\n:2\r\n:3\r\n");

        var values = new List<long>();
        for (int i = 0; i < 3; i++)
        {
            values.Add((await RespReader.ReadAsync(pipe.Reader).AsTask().WaitAsync(Timeout))!.AsLong());
        }

        values.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Inline_command_read_with_server_options()
    {
        var pipe = new Pipe();
        await WriteAsync(pipe, "PING\r\n");

        RespValue? value = await RespReader.ReadAsync(pipe.Reader, RespReaderOptions.Server).AsTask().WaitAsync(Timeout);

        value!.AsStringList().Should().Equal("PING");
    }

    [Fact]
    public async Task Malformed_input_throws_protocol_exception()
    {
        var pipe = new Pipe();
        await WriteAsync(pipe, ":abc\r\n");

        Func<Task> act = () => RespReader.ReadAsync(pipe.Reader).AsTask().WaitAsync(Timeout);

        await act.Should().ThrowAsync<RespProtocolException>();
    }

    [Fact]
    public async Task Cancellation_while_waiting_throws()
    {
        var pipe = new Pipe();
        using var cts = new CancellationTokenSource();

        Task<RespValue?> read = RespReader.ReadAsync(pipe.Reader, cancellationToken: cts.Token).AsTask();
        cts.Cancel();

        Func<Task> act = () => read.WaitAsync(Timeout);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
