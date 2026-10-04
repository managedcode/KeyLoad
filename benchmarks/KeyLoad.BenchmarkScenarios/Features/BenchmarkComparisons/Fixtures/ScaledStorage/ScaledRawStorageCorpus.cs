using System.Buffers.Binary;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Generates compact immutable scale-v1 keys and deterministic values for one dataset.</summary>
internal sealed class ScaledRawStorageCorpus
{
    private const int KeyBytes = 16;
    private const int MinimumRecordCount = 1;
    private const int MaximumRecordCount = 5_000_000;
    private const int SmallValueBytes = 32;
    private const int LargeValueBytes = 1024;
    private const int CancellationCheckStride = 256;
    private const ulong Seed = 1729;
    private const uint Version = 1;
    private const int TailOffset = 32;
    private const long TailIndexMultiplier = 31;
    private const long TailOffsetMultiplier = 17;
    private const int TailFirstIndexShift = 8;
    private const int TailSecondIndexShift = 16;
    private const string ValueLengthMessage = "The destination must match the configured value length exactly.";
    private readonly byte[] keys;

    /// <summary>Allocates one pinned key slab and fills each seeded and reserved key once.</summary>
    public ScaledRawStorageCorpus(int recordCount, int valueBytes, CancellationToken cancellationToken = default)
    {
        ValidateArguments(recordCount, valueBytes);
        cancellationToken.ThrowIfCancellationRequested();
        RecordCount = recordCount;
        ValueBytes = valueBytes;
        var keyCount = checked(recordCount + 1);
        keys = GC.AllocateArray<byte>(checked(keyCount * KeyBytes), pinned: true);
        FillKeys(keyCount, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Gets the number of seeded records; the next index is reserved for a miss.</summary>
    public int RecordCount { get; }

    /// <summary>Gets the exact generated value length.</summary>
    public int ValueBytes { get; }

    /// <summary>Gets the retained bytes in the single pinned key slab.</summary>
    public long RetainedKeyBytes => keys.LongLength;

    /// <summary>Returns the stable big-endian key for a seeded or reserved index.</summary>
    public Memory<byte> Key(int index)
    {
        ValidateIndex(index);
        return keys.AsMemory(checked(index * KeyBytes), KeyBytes);
    }

    /// <summary>Writes a complete deterministic value into an exactly sized caller span.</summary>
    public void WriteValue(int index, Span<byte> destination)
    {
        ValidateIndex(index);
        if (destination.Length != ValueBytes)
        {
            throw new ArgumentException(ValueLengthMessage, nameof(destination));
        }

        WriteHeader(index, destination);
        FillTail(index, destination);
    }

    private static void ValidateArguments(int recordCount, int valueBytes)
    {
        if (recordCount is < MinimumRecordCount or > MaximumRecordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(recordCount));
        }

        if (valueBytes is not (SmallValueBytes or LargeValueBytes))
        {
            throw new ArgumentOutOfRangeException(nameof(valueBytes));
        }
    }

    private void ValidateIndex(int index)
    {
        if ((uint)index > (uint)RecordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    private void FillKeys(int keyCount, CancellationToken cancellationToken)
    {
        for (var index = 0; index < keyCount; index++)
        {
            if (index % CancellationCheckStride == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var key = keys.AsSpan(index * KeyBytes, KeyBytes);
            BinaryPrimitives.WriteUInt64BigEndian(key, Seed);
            BinaryPrimitives.WriteUInt64BigEndian(key[sizeof(ulong)..], (ulong)index);
        }
    }

    private static void WriteHeader(int index, Span<byte> destination)
    {
        var recordIndex = (ulong)index;
        BinaryPrimitives.WriteUInt64BigEndian(destination, recordIndex);
        BinaryPrimitives.WriteUInt64BigEndian(destination[sizeof(ulong)..], Seed);
        BinaryPrimitives.WriteUInt32BigEndian(destination[(sizeof(ulong) * 2)..], Version);
        BinaryPrimitives.WriteUInt32BigEndian(destination[((sizeof(ulong) * 2) + sizeof(uint))..],
            (uint)destination.Length);
        BinaryPrimitives.WriteUInt64BigEndian(destination[((sizeof(ulong) * 2) + (sizeof(uint) * 2))..],
            ~recordIndex);
    }

    private static void FillTail(int index, Span<byte> destination)
    {
        for (var offset = TailOffset; offset < destination.Length; offset++)
        {
            destination[offset] = unchecked((byte)((long)Seed + (index * TailIndexMultiplier)
                + (offset * TailOffsetMultiplier) + (index >> TailFirstIndexShift)
                + (index >> TailSecondIndexShift)));
        }
    }
}
