namespace KeyLoad.Comparisons.Targets;

internal sealed record OpenSearchClusterObservation(string Status, string[] NodeIds, string[] ShardPlacements,
    int DataNodes, int ActiveCopies, string ClusterManager)
{
    internal ClusterEvidence ToClusterEvidence(string version, OpenSearchClusterObservation beforeSeed, string settings, int expectedCopies)
        => new(NodeIds.Length, ActiveCopies, Status,
        [
            OpenSearchNames.EvidenceVersion + version,
            OpenSearchNames.EvidenceDataNodes + DataNodes,
            OpenSearchNames.EvidenceClusterManager + ClusterManager,
            OpenSearchNames.EvidenceNodeIds + string.Join(OpenSearchNames.CommaSeparator, NodeIds.Order(StringComparer.Ordinal)),
            OpenSearchNames.EvidencePlacements + string.Join(OpenSearchNames.CommaSeparator, ShardPlacements.Order(StringComparer.Ordinal)),
            OpenSearchNames.EvidenceCopies + ActiveCopies + OpenSearchNames.CopyCountSeparator + expectedCopies,
            OpenSearchNames.EvidenceSettings + settings,
            OpenSearchNames.EvidencePreSeed + string.Join(OpenSearchNames.CommaSeparator, beforeSeed.ShardPlacements.Order(StringComparer.Ordinal)),
            OpenSearchNames.EvidenceProbe
        ]);
}
