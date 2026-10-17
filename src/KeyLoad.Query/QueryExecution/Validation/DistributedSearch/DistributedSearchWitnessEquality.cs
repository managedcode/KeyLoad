namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedSearchWitnessEquality
{
    internal static bool Same(DistributedTextWitnessV1 expected, DistributedTextWitnessV1 actual)
        => expected is not null && actual is not null && expected.Owner is not null && actual.Owner is not null
            && expected.Statistics is not null && actual.Statistics is not null
            && expected.Partition == actual.Partition && SameOwner(expected.Owner, actual.Owner)
            && expected.NodeId == actual.NodeId && expected.Incarnation == actual.Incarnation
            && expected.ReadGeneration == actual.ReadGeneration && expected.CutPosition == actual.CutPosition
            && expected.PolicyEpoch == actual.PolicyEpoch && expected.SchemaVersion == actual.SchemaVersion
            && string.Equals(expected.ResourcePolicyDigest, actual.ResourcePolicyDigest, StringComparison.Ordinal)
            && expected.ReadBytes == actual.ReadBytes && expected.ExaminedRecords == actual.ExaminedRecords
            && SameStatistics(expected.Statistics, actual.Statistics);

    internal static bool SameOwner(PhysicalShardRecord expected, PhysicalShardRecord actual)
        => expected.PhysicalShardId == actual.PhysicalShardId && expected.Incarnation == actual.Incarnation
            && expected.PlacementEpoch == actual.PlacementEpoch
            && expected.VoterIds.SequenceEqual(actual.VoterIds, StringComparer.Ordinal);

    private static bool SameStatistics(DistributedTextStatisticsV1 expected, DistributedTextStatisticsV1 actual)
        => !expected.Terms.IsDefault && !actual.Terms.IsDefault
            && !expected.DocumentFrequencies.IsDefault && !actual.DocumentFrequencies.IsDefault
            && expected.DocumentCount == actual.DocumentCount && expected.TotalLength == actual.TotalLength
            && expected.Terms.SequenceEqual(actual.Terms, StringComparer.Ordinal)
            && expected.DocumentFrequencies.SequenceEqual(actual.DocumentFrequencies);
}
