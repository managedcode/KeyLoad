using System.Buffers.Binary;
using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class NativeStorePositionOracle
{
    internal static byte[] Key => KeyCodec.Encode("system", "position");

    internal static byte[] Value(long position)
    {
        var bytes = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, position);
        return bytes;
    }

    internal static byte[] AtPosition(byte[] original, long position)
    {
        var page = OrleansSerialization.Deserialize<ScanPage>(original);
        var key = Key;
        var records = page.Records.Select(record => record.Key.Span.SequenceEqual(key)
            ? record with { Value = Value(position) } : record).ToImmutableArray();
        return OrleansSerialization.Serialize(page with { Records = records });
    }

    internal static (string Key, string Value)[] AtPosition((string Key, string Value)[] original, long position)
    {
        var key = Convert.ToHexString(Key);
        var value = Convert.ToHexString(Value(position));
        return original.Select(row => row.Key == key ? (row.Key, value) : row).ToArray();
    }

    internal static string AtPosition(string original, long position)
    {
        var rows = System.Text.Json.JsonSerializer.Deserialize<string[][]>(Convert.FromHexString(original), JsonDefaults.Options)
            ?? throw new InvalidOperationException("The original native store image is absent.");
        var key = Convert.ToHexString(Key);
        var value = Convert.ToHexString(Value(position));
        return Convert.ToHexString(JsonDefaults.Serialize(rows.Select(row => row[0] == key ? new[] { key, value } : row).ToArray()));
    }
}
