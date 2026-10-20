namespace KeyLoad.Core.Features.Search;

internal static class Bm25CorpusShape
{
    private const long UninitializedPublicationVersion = 0;
    private const long InitialPolicyEpoch = 0;
    private const long MinimumCut = 0;
    private const long MinimumOutboxTail = 0;
    private const int MinimumDocumentCount = 0;
    private const long MinimumCorpusLength = 0;
    private const int FirstTermIndex = 0;
    private const int MinimumDocumentFrequency = 0;
    private const int Sha256HexLength = 64;
    private const char LowestDecimalDigit = '0';
    private const char HighestDecimalDigit = '9';
    private const char LowestHexLetter = 'a';
    private const char HighestHexLetter = 'f';

    internal static void Require(Bm25CorpusCurrent current, DatabaseLimits limits)
    {
        if (current is null || current.Partition is null || current.SourceOwner is null
            || current.Statistics is null || current.FormatVersion != Bm25CorpusProtocol.Format
            || current.Generation == Guid.Empty || current.Incarnation == Guid.Empty
            || current.SourceOwner.Incarnation != current.Incarnation
            || current.SourceOwner.PhysicalShardId == Guid.Empty
            || current.PolicyEpoch < InitialPolicyEpoch || current.SchemaVersion <= UninitializedPublicationVersion
            || current.SourceOutboxTail < MinimumOutboxTail || current.CapturePosition < MinimumCut || current.CaptureApplied < MinimumCut
            || current.CaptureReadGeneration < MinimumCut || string.IsNullOrWhiteSpace(current.Field)
            || string.IsNullOrWhiteSpace(current.TokenizerVersion) || !Digest(current.ResourcePolicyDigest)
            || !Digest(current.PayloadDigest))
        {
            throw Errors.Fail(ErrorCode.Corruption, Bm25CorpusProtocol.Invalid);
        }
        EngineResourceAccess.ValidatePartition(current.Partition);
        JsonData.Identifier(current.Collection);
        JsonData.Identifier(current.PrincipalId);
        var statistics = current.Statistics;
        if (statistics.Terms.IsDefault || statistics.DocumentFrequencies.IsDefault
            || statistics.Terms.Length != statistics.DocumentFrequencies.Length
            || statistics.DocumentCount < MinimumDocumentCount || statistics.DocumentCount > limits.MaxScanRecords
            || statistics.TotalLength < MinimumCorpusLength || statistics.Terms.Length > limits.MaxScanRecords)
        {
            throw Errors.Fail(ErrorCode.Corruption, Bm25CorpusProtocol.Invalid);
        }
        var terms = new HashSet<string>(StringComparer.Ordinal);
        for (var index = FirstTermIndex; index < statistics.Terms.Length; index++)
        {
            if (string.IsNullOrEmpty(statistics.Terms[index]) || !terms.Add(statistics.Terms[index])
                || statistics.DocumentFrequencies[index] < MinimumDocumentFrequency
                || statistics.DocumentFrequencies[index] > statistics.DocumentCount)
            {
                throw Errors.Fail(ErrorCode.Corruption, Bm25CorpusProtocol.Invalid);
            }
        }
        if (statistics.DocumentCount == MinimumDocumentCount && (statistics.TotalLength != MinimumCorpusLength
            || statistics.DocumentFrequencies.Any(frequency => frequency != MinimumDocumentFrequency)))
        {
            throw Errors.Fail(ErrorCode.Corruption, Bm25CorpusProtocol.Invalid);
        }
    }

    internal static void RequirePhase(Bm25CorpusPublicationPhaseCommand phase, DatabaseLimits limits)
    {
        if (phase is null || phase.Current is null || phase.CommandId == Guid.Empty
            || phase.CommandId != phase.Current.Generation || phase.SourcePlacement is null
            || phase.SourcePlacement.Partition != phase.Current.Partition
            || phase.SourcePlacement.PhysicalShardId != phase.Current.SourceOwner?.PhysicalShardId
            || phase.SourcePlacement.Incarnation != phase.Current.Incarnation
            || phase.ExpectedGeneration == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Corruption, Bm25CorpusProtocol.Invalid);
        }
        Require(phase.Current, limits);
        if (phase.Current.PayloadDigest != Bm25CorpusDigest.Compute(phase.Current, phase.SourcePlacement,
            limits.MaxBatchBytes))
        {
            throw Errors.Fail(ErrorCode.Corruption, Bm25CorpusProtocol.Invalid);
        }
    }

    internal static Bm25CorpusCurrent RequireRecord(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value,
        PartitionRef partition, DatabaseLimits limits)
    {
        if (checked((long)key.Length + value.Length) > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Bm25CorpusProtocol.Capacity);
        }
        var current = OrleansSerialization.Deserialize<Bm25CorpusCurrent>(value);
        Require(current, limits);
        if (current.Partition != partition || !key.SequenceEqual(Bm25CorpusKeys.Current(current)))
        {
            throw Errors.Fail(ErrorCode.Corruption, Bm25CorpusProtocol.Invalid);
        }
        return current;
    }

    private static bool Digest(string? value)
        => value is { Length: Sha256HexLength }
            && value.All(character => character is >= LowestDecimalDigit and <= HighestDecimalDigit or >= LowestHexLetter and <= HighestHexLetter);
}
