using System.Buffers;

namespace Cinnabar.Protocol.Tests;

/// <summary>Values arriving in pieces, as they do over TCP.</summary>
public class RespReaderFragmentationTests
{
    public static TheoryData<string> Samples => new()
    {
        "+OK\r\n",
        "-ERR boom\r\n",
        ":-12345\r\n",
        "$5\r\nhello\r\n",
        "$0\r\n\r\n",
        "$-1\r\n",
        "*-1\r\n",
        "*0\r\n",
        "$6\r\n日本\r\n",
        "*3\r\n$3\r\nSET\r\n$1\r\nk\r\n$3\r\n€\r\n",
        "*2\r\n*2\r\n:1\r\n+a\r\n*1\r\n$-1\r\n",
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void Every_strict_prefix_needs_more_data_and_leaves_buffer_untouched(string wire)
    {
        byte[] bytes = Resp.Bytes(wire);

        for (int length = 0; length < bytes.Length; length++)
        {
            var buffer = Resp.Sequence(bytes[..length]);
            ReadOnlySequence<byte> before = buffer;

            bool parsed = RespReader.TryRead(ref buffer, out RespValue? value);

            parsed.Should().BeFalse($"prefix of {length} bytes is incomplete");
            value.Should().BeNull();
            buffer.Start.Should().Be(before.Start);
            buffer.Length.Should().Be(before.Length);
        }
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Complete_value_consumes_exactly_its_bytes(string wire)
    {
        byte[] bytes = Resp.Bytes(wire + ":99\r\n");
        var buffer = Resp.Sequence(bytes);

        RespReader.TryRead(ref buffer, out RespValue? value).Should().BeTrue();

        value.Should().NotBeNull();
        buffer.ToArray().Should().Equal(Resp.Bytes(":99\r\n"));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Two_segments_split_at_every_boundary_parse_like_one(string wire)
    {
        byte[] bytes = Resp.Bytes(wire);
        string expected = Resp.ParseSingle(bytes).ToString();

        for (int split = 0; split <= bytes.Length; split++)
        {
            var buffer = Resp.MultiSegment(bytes[..split], bytes[split..]);

            RespReader.TryRead(ref buffer, out RespValue? value).Should().BeTrue($"split at {split}");

            value!.ToString().Should().Be(expected);
            buffer.Length.Should().Be(0);
        }
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void One_byte_segments_parse_like_one(string wire)
    {
        byte[] bytes = Resp.Bytes(wire);
        var buffer = Resp.OneBytePerSegment(bytes);

        RespReader.TryRead(ref buffer, out RespValue? value).Should().BeTrue();

        value!.ToString().Should().Be(Resp.ParseSingle(bytes).ToString());
        buffer.Length.Should().Be(0);
    }

    [Fact]
    public void Multi_byte_character_split_across_segments_is_decoded()
    {
        byte[] bytes = Resp.Bytes("$3\r\n€\r\n");
        // Split inside the three bytes of the euro sign.
        var buffer = Resp.MultiSegment(bytes[..5], bytes[5..6], bytes[6..]);

        RespReader.TryRead(ref buffer, out RespValue? value).Should().BeTrue();

        value!.Text.Should().Be("€");
    }

    [Fact]
    public void Pipelined_values_across_segments_are_read_in_order()
    {
        byte[] bytes = Resp.Bytes("+A\r\n$2\r\nbc\r\n:3\r\n");
        var buffer = Resp.MultiSegment(bytes[..3], bytes[3..9], bytes[9..]);
        var values = new List<string>();

        while (RespReader.TryRead(ref buffer, out RespValue? value))
        {
            values.Add(value!.ToString());
        }

        values.Should().Equal("+A", "\"bc\"", ":3");
    }

    [Fact]
    public void Array_with_complete_header_but_missing_elements_is_incomplete()
    {
        var buffer = Resp.Sequence("*3\r\n:1\r\n:2\r\n");

        RespReader.TryRead(ref buffer, out _).Should().BeFalse();
        buffer.Length.Should().Be(Resp.Bytes("*3\r\n:1\r\n:2\r\n").Length);
    }
}
