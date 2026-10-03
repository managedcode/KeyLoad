using System.Text;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkWireData
{
    internal static ReadOnlyMemory<byte> Memory(byte[] bytes) => new(bytes);

    internal static byte[] Uleb(ulong value)
    {
        Span<byte> scratch = stackalloc byte[10];
        var written = 0;
        while (value >= 0x80)
        {
            scratch[written++] = (byte)(value | 0x80);
            value >>= 7;
        }
        scratch[written++] = (byte)value;
        return scratch[..written].ToArray();
    }

    internal static byte[] MakeNonMinimal(ReadOnlySpan<byte> value)
    {
        var length = 1;
        while ((value[length - 1] & 0x80) != 0)
        {
            length++;
        }
        var result = new byte[length + 1];
        value[..length].CopyTo(result);
        result[length - 1] |= 0x80;
        result[length] = 0;
        return result;
    }

    internal static byte[] TagDictionaryWithUnreferencedEntry()
    {
        var first = Encoding.UTF8.GetBytes("{}");
        var second = Encoding.UTF8.GetBytes("{\"x\":1}");
        using var stream = new MemoryStream();
        stream.WriteByte(2);
        WriteFramed(first, stream);
        WriteFramed(second, stream);
        stream.WriteByte(0);
        stream.WriteByte(0);
        return stream.ToArray();
    }

    internal static byte[] TagDictionaryWithInvalidIndexes()
    {
        var value = Encoding.UTF8.GetBytes("{}");
        using var stream = new MemoryStream();
        stream.WriteByte(1);
        WriteFramed(value, stream);
        stream.WriteByte(1);
        stream.WriteByte(1);
        return stream.ToArray();
    }

    internal static byte[] TagDictionaryWithDuplicates()
    {
        var value = Encoding.UTF8.GetBytes("{}");
        using var stream = new MemoryStream();
        stream.WriteByte(2);
        WriteFramed(value, stream);
        WriteFramed(value, stream);
        stream.WriteByte(0);
        stream.WriteByte(0);
        return stream.ToArray();
    }

    internal static byte[] TagDictionaryWithOutOfOrderIndexes()
    {
        var first = Encoding.UTF8.GetBytes("{}");
        var second = Encoding.UTF8.GetBytes("{\"x\":1}");
        using var stream = new MemoryStream();
        stream.WriteByte(2);
        WriteFramed(first, stream);
        WriteFramed(second, stream);
        stream.WriteByte(1);
        stream.WriteByte(0);
        return stream.ToArray();
    }

    private static void WriteFramed(byte[] value, Stream destination)
    {
        destination.WriteByte(checked((byte)(value.Length << 1)));
        destination.Write(value);
    }
}
