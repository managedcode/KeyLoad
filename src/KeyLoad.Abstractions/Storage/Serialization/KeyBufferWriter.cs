using System.Buffers;

namespace KeyLoad.Storage;

/// <summary>Appends key bytes to a caller-supplied stack buffer and grows into pooled arrays; the final key is one exact copy.</summary>
internal ref struct KeyBufferWriter
{
    private const int GrowthFactor = 2;
    private Span<byte> buffer;
    private byte[]? rented;
    private int written;

    internal KeyBufferWriter(Span<byte> initial)
    {
        buffer = initial;
    }

    internal void Write(byte value)
    {
        Ensure(sizeof(byte));
        buffer[written++] = value;
    }

    internal void Write(scoped ReadOnlySpan<byte> bytes)
    {
        Ensure(bytes.Length);
        bytes.CopyTo(buffer[written..]);
        written += bytes.Length;
    }

    /// <summary>Writes bytes with every zero byte followed by the escape marker, then the two-zero terminator.</summary>
    internal void WriteEscaped(scoped ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            Write(b);
            if (b == KeyCodecTokens.EscapedZeroByte)
            {
                Write((byte)KeyCodecTokens.ZeroEscapeMarker);
            }
        }
        Write((byte)KeyCodecTokens.EscapedZeroByte);
        Write((byte)KeyCodecTokens.EscapedZeroByte);
    }

    internal readonly byte[] ToArray() => buffer[..written].ToArray();

    internal void Dispose()
    {
        if (rented is { } array)
        {
            rented = null;
            ArrayPool<byte>.Shared.Return(array);
        }
    }

    private void Ensure(int count)
    {
        if (written + count <= buffer.Length)
        {
            return;
        }
        var next = ArrayPool<byte>.Shared.Rent(Math.Max(checked(buffer.Length * GrowthFactor), checked(written + count)));
        buffer[..written].CopyTo(next);
        var previous = rented;
        rented = next;
        buffer = next;
        if (previous is not null)
        {
            ArrayPool<byte>.Shared.Return(previous);
        }
    }
}
