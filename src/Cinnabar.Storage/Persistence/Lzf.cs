namespace Cinnabar.Storage.Persistence;

/// <summary>
/// LZF decompression. Redis compresses longer strings in RDB files with LZF
/// (encoding type <c>0xC3</c>).
/// </summary>
internal static class Lzf
{
    /// <summary>Decompresses <paramref name="input"/> into exactly <paramref name="outputLength"/> bytes.</summary>
    /// <exception cref="InvalidDataException">The data is corrupt.</exception>
    public static byte[] Decompress(ReadOnlySpan<byte> input, int outputLength)
    {
        var output = new byte[outputLength];
        int ip = 0;
        int op = 0;

        while (ip < input.Length)
        {
            int ctrl = input[ip++];

            if (ctrl < 32)
            {
                // Literal run of ctrl + 1 bytes.
                int length = ctrl + 1;
                if (ip + length > input.Length || op + length > output.Length)
                {
                    throw new InvalidDataException("Corrupt LZF data: literal run out of bounds.");
                }

                input.Slice(ip, length).CopyTo(output.AsSpan(op));
                ip += length;
                op += length;
                continue;
            }

            // Back reference: copy `length` bytes from earlier in the output.
            int lengthCode = ctrl >> 5;
            if (lengthCode == 7)
            {
                if (ip >= input.Length)
                {
                    throw new InvalidDataException("Corrupt LZF data: truncated back reference.");
                }

                lengthCode += input[ip++];
            }

            if (ip >= input.Length)
            {
                throw new InvalidDataException("Corrupt LZF data: truncated back reference.");
            }

            int reference = op - ((ctrl & 0x1F) << 8) - 1 - input[ip++];
            int copyLength = lengthCode + 2;
            if (reference < 0 || op + copyLength > output.Length)
            {
                throw new InvalidDataException("Corrupt LZF data: back reference out of bounds.");
            }

            // Byte-by-byte on purpose: the source and destination may overlap.
            for (int i = 0; i < copyLength; i++)
            {
                output[op++] = output[reference++];
            }
        }

        if (op != outputLength)
        {
            throw new InvalidDataException($"Corrupt LZF data: expected {outputLength} bytes, got {op}.");
        }

        return output;
    }
}
