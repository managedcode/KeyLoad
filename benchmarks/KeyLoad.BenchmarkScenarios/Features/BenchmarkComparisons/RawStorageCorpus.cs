using System.Buffers.Binary;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Retains the immutable deterministic binary inputs used by one ZoneTree fixture.</summary>
internal sealed class RawStorageCorpus
{
    private const int KeyLength = 16;
    private const int Seed = 1729;
    private const int AlternateSalt = 13;
    private const int OriginalMarker = 0x5A;
    private const int AlternateMarker = 0xA5;
    private const int ValueSeedBytes = 4;
    private const int MinimumRecordCount = 1;
    private const int MaximumRecordCount = 4096;
    private const int SmallPayloadBytes = 32;
    private const int LargePayloadBytes = 1024;
    private readonly Memory<byte>[] keys;
    private readonly Memory<byte>[] originalValues;
    private readonly Memory<byte>[] alternateValues;

    /// <summary>Creates a corpus containing every seeded and reserved valid index.</summary>
    public RawStorageCorpus(int recordCount, int valueBytes)
    {
        Validate(recordCount, valueBytes);
        RecordCount = recordCount;
        ValueBytes = valueBytes;
        var validIndexCount = checked(recordCount + 2);
        keys = new Memory<byte>[validIndexCount];
        originalValues = new Memory<byte>[validIndexCount];
        alternateValues = new Memory<byte>[validIndexCount];
        for (var index = 0; index < validIndexCount; index++)
        {
            keys[index] = CreateKey(index);
            originalValues[index] = CreateValue(index, valueBytes, alternate: false);
            alternateValues[index] = CreateValue(index, valueBytes, alternate: true);
        }
    }

    /// <summary>Gets the number of records seeded by a fixture.</summary>
    public int RecordCount { get; }

    /// <summary>Gets the byte length of each value.</summary>
    public int ValueBytes { get; }

    /// <summary>Returns the stable pinned key for a seeded or reserved index.</summary>
    public Memory<byte> Key(int index) => keys[ValidateIndex(index)];

    /// <summary>Returns the immutable original value bytes for a seeded or reserved index.</summary>
    public Memory<byte> Value(int index, bool alternate = false)
        => alternate ? alternateValues[ValidateIndex(index)] : originalValues[ValidateIndex(index)];

    private static void Validate(int recordCount, int valueBytes)
    {
        if (recordCount is < MinimumRecordCount or > MaximumRecordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(recordCount));
        }

        if (valueBytes is not (SmallPayloadBytes or LargePayloadBytes))
        {
            throw new ArgumentOutOfRangeException(nameof(valueBytes));
        }
    }

    private int ValidateIndex(int index)
    {
        if ((uint)index >= (uint)keys.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return index;
    }

    private static Memory<byte> CreateKey(int index)
    {
        var key = GC.AllocateArray<byte>(KeyLength, pinned: true);
        BinaryPrimitives.WriteUInt64LittleEndian(key.AsSpan(0, sizeof(ulong)), Seed);
        BinaryPrimitives.WriteUInt64LittleEndian(key.AsSpan(sizeof(ulong), sizeof(ulong)), (ulong)index);
        return key;
    }

    private static Memory<byte> CreateValue(int index, int valueBytes, bool alternate)
    {
        var value = new byte[valueBytes];
        var salt = alternate ? AlternateSalt : 0;
        value[0] = unchecked((byte)(Seed + index + salt));
        value[1] = alternate ? (byte)AlternateMarker : (byte)OriginalMarker;
        value[2] = 0;
        value[3] = byte.MaxValue;
        for (var offset = ValueSeedBytes; offset < value.Length; offset++)
        {
            value[offset] = unchecked((byte)(Seed + (index * 31) + (offset * 17) + salt));
        }

        return value;
    }
}
