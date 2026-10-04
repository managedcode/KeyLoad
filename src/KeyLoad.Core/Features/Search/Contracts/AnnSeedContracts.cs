using System.Collections.Immutable;

namespace KeyLoad.Core.Features.Search;

internal sealed record AnnSeedOptions
{
    private const string InvalidOptions = "The ANN seed limits are invalid.";
    internal const int DefaultMaxRecords = 5_000_000;
    internal const long DefaultMaxOwnedBytes = 268_435_456;
    internal const long DefaultMaxPeakBytes = 536_870_912;
    internal const long DefaultMaxWorkUnits = 1_000_000_000;

    internal int MaxRecords { get; init; } = DefaultMaxRecords;
    internal long MaxOwnedBytes { get; init; } = DefaultMaxOwnedBytes;
    internal long MaxPeakBytes { get; init; } = DefaultMaxPeakBytes;
    internal long MaxWorkUnits { get; init; } = DefaultMaxWorkUnits;

    internal static void ValidateInput(AnnSeedOptions? options)
    {
        if (options is null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidOptions);
        }
        options.Validate();
    }

    internal void Validate()
    {
        if (MaxRecords is < 1 or > DefaultMaxRecords
            || MaxOwnedBytes is < 1_024 or > 8_589_934_592
            || MaxPeakBytes is < 1_024 or > 17_179_869_184
            || MaxWorkUnits is < 1 or > 1_000_000_000_000
            || MaxPeakBytes < MaxOwnedBytes)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidOptions);
        }
    }
}

internal readonly record struct AnnSeedCut(Guid NodeId, Guid Incarnation,
    int StoreFormatVersion, int KeyCodecVersion, long ReadGeneration,
    long Position, long AppliedPosition, long OutboxTail, long OutboxFirstAvailable);

internal sealed record AnnSeedScope(string PrincipalId, long PolicyEpoch,
    PartitionRef Partition, string Collection, string Field, long SchemaVersion,
    VectorSpace Space, DateTimeOffset EvaluatedAt);

internal sealed record AnnSeed(AnnSeedScope Scope, AnnSeedCut Cut,
    ImmutableArray<VectorRecord> Records, string CorpusSha256,
    long OwnedBytesUpperBound, long PeakBytesUpperBound, long ReadBytes, long WorkUnits);

internal sealed record AnnSeedCaptured(AnnSeedScope Scope, AnnSeedCut Cut,
    ImmutableArray<VectorRecord> Records, byte[] HashScratch, long OwnedBytes, long PeakBytes, AnnSeedWork Work);
