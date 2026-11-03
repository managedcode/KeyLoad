using System.Runtime.InteropServices;

namespace KeyLoad;

/// <summary>The canonical vector row, with length-prefixed little-endian IEEE 754 coordinates.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(VectorStorageRecord.StorageAlias)]
internal sealed record VectorStorageRecord(
    [property: Orleans.Id(0)] string DocumentId,
    [property: Orleans.Id(1)] string Field,
    [property: Orleans.Id(2)] VectorSpace Space,
    [property: Orleans.Id(3)] byte[] PackedCoordinates,
    [property: Orleans.Id(4)] long DocumentRevision)
{
    private const string StorageAlias = "keyload.search.vector-storage-record";
    private const string InvalidCoordinates = "The stored vector coordinates are corrupt.";
    private const string LittleEndianRequired = "Packed vector storage requires a little-endian runtime.";

    internal static byte[] Pack(ReadOnlySpan<float> values)
    {
        RequireLittleEndian();
        return MemoryMarshal.AsBytes(values).ToArray();
    }

    internal ReadOnlySpan<float> Coordinates()
    {
        RequireLittleEndian();
        if (PackedCoordinates is null || PackedCoordinates.Length % sizeof(float) != 0)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidCoordinates);
        }
        return MemoryMarshal.Cast<byte, float>((ReadOnlySpan<byte>)PackedCoordinates);
    }

    internal VectorRecord Materialize() => new(DocumentId, Field, Space,
        ImmutableCollectionsMarshal.AsImmutableArray(Coordinates().ToArray()), DocumentRevision);

    private static void RequireLittleEndian()
    {
        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException(LittleEndianRequired);
        }
    }
}
