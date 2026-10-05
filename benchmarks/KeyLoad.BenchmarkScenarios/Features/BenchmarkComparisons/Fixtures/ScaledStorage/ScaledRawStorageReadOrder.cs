using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Retains one deterministic shuffled permutation and independent wrapping cursors.</summary>
internal sealed class ScaledRawStorageReadOrder
{
    private const int MinimumRecordCount = 1;
    private const int MaximumRecordCount = 1_000_000;
    private const int CancellationCheckStride = 256;
    private const int EncodedIndexBytes = sizeof(int);
    private const ulong InitialState = 1729;
    private const ulong StateIncrement = 0x9e3779b97f4a7c15;
    private const ulong FirstMultiplier = 0xbf58476d1ce4e5b9;
    private const ulong SecondMultiplier = 0x94d049bb133111eb;
    private readonly int[] permutation;
    private int sequentialCursor;
    private int randomCursor;

    /// <summary>Builds the scale-v1 SplitMix64 Fisher-Yates order within the supplied cancellation scope.</summary>
    public ScaledRawStorageReadOrder(int recordCount, CancellationToken cancellationToken = default)
    {
        if (recordCount is < MinimumRecordCount or > MaximumRecordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(recordCount));
        }

        cancellationToken.ThrowIfCancellationRequested();
        RecordCount = recordCount;
        permutation = new int[recordCount];
        InitializePermutation(cancellationToken);
        ShufflePermutation(cancellationToken);
        PermutationDigest = ComputePermutationDigest(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Gets the number of distinct indices in one traversal.</summary>
    public int RecordCount { get; }

    /// <summary>Gets bytes retained by the single permutation array.</summary>
    public long RetainedOrderBytes => (long)permutation.Length * EncodedIndexBytes;

    /// <summary>Gets lowercase SHA-256 over the permutation's little-endian Int32 values.</summary>
    public string PermutationDigest { get; }

    /// <summary>Returns the next ascending index and wraps after the last record.</summary>
    public int NextSequential()
    {
        var value = sequentialCursor;
        sequentialCursor = value + 1 == RecordCount ? 0 : value + 1;
        return value;
    }

    /// <summary>Returns the next shuffled index and wraps after one complete permutation.</summary>
    public int NextRandom()
    {
        var value = permutation[randomCursor];
        randomCursor = randomCursor + 1 == RecordCount ? 0 : randomCursor + 1;
        return value;
    }

    private void InitializePermutation(CancellationToken cancellationToken)
    {
        for (var index = 0; index < permutation.Length; index++)
        {
            CheckCancellationAtBoundary(index, cancellationToken);
            permutation[index] = index;
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private void ShufflePermutation(CancellationToken cancellationToken)
    {
        var state = InitialState;
        var operation = 0;
        for (var index = permutation.Length - 1; index > 0; index--)
        {
            CheckCancellationAtBoundary(operation++, cancellationToken);
            var otherIndex = (int)(NextSplitMix64(ref state) % (ulong)(index + 1));
            (permutation[index], permutation[otherIndex]) = (permutation[otherIndex], permutation[index]);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private string ComputePermutationDigest(CancellationToken cancellationToken)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> encodedIndex = stackalloc byte[EncodedIndexBytes];
        for (var index = 0; index < permutation.Length; index++)
        {
            CheckCancellationAtBoundary(index, cancellationToken);
            BinaryPrimitives.WriteInt32LittleEndian(encodedIndex, permutation[index]);
            digest.AppendData(encodedIndex);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    private static void CheckCancellationAtBoundary(int operation, CancellationToken cancellationToken)
    {
        if (operation % CancellationCheckStride == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static ulong NextSplitMix64(ref ulong state)
    {
        state = unchecked(state + StateIncrement);
        var value = state;
        value = unchecked((value ^ (value >> 30)) * FirstMultiplier);
        value = unchecked((value ^ (value >> 27)) * SecondMultiplier);
        return value ^ (value >> 31);
    }
}
