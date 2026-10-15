using System.Buffers;
using System.Text;

namespace Cinnabar.Protocol.Tests;

/// <summary>Helpers for building byte sequences and parsing them in tests.</summary>
internal static class Resp
{
    public static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    public static ReadOnlySequence<byte> Sequence(string text) => new(Bytes(text));

    public static ReadOnlySequence<byte> Sequence(byte[] bytes) => new(bytes);

    /// <summary>Builds a sequence made of one segment per chunk.</summary>
    public static ReadOnlySequence<byte> MultiSegment(params byte[][] chunks)
    {
        if (chunks.Length == 0)
        {
            return ReadOnlySequence<byte>.Empty;
        }

        var first = new Segment(chunks[0]);
        Segment last = first;
        for (int i = 1; i < chunks.Length; i++)
        {
            last = last.Append(chunks[i]);
        }

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    /// <summary>Splits <paramref name="bytes"/> into one-byte segments.</summary>
    public static ReadOnlySequence<byte> OneBytePerSegment(byte[] bytes) =>
        MultiSegment(bytes.Select(b => new[] { b }).ToArray());

    /// <summary>Parses exactly one value from <paramref name="text"/>, asserting it consumes everything.</summary>
    public static RespValue ParseSingle(string text, RespReaderOptions? options = null) =>
        ParseSingle(Bytes(text), options);

    public static RespValue ParseSingle(byte[] bytes, RespReaderOptions? options = null)
    {
        ReadOnlySequence<byte> buffer = Sequence(bytes);
        RespReader.TryRead(ref buffer, out RespValue? value, options).Should().BeTrue();
        buffer.Length.Should().Be(0, "the whole value should be consumed");
        value.Should().NotBeNull();
        return value!;
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
