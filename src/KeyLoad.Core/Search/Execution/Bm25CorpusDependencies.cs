using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Search;

internal static class Bm25CorpusDependencies
{
    internal static bool Matches(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, Bm25CorpusCurrentV1 current, AtomicPartitionPlacementResolution placement,
        long maximumBytes, ReadExecutionBudget? work = null)
    {
        var owner = database.Store.Identity;
        return current.PrincipalId == principal.Id && current.PolicyEpoch == principal.PolicyEpoch
            && current.SchemaVersion == resource.SchemaVersion
            && current.ResourcePolicyDigest == (work is null ? Bm25CorpusDigest.Resource(resource, maximumBytes)
                : Bm25CorpusDigest.Resource(resource, work))
            && current.SourceOutboxTail == database.ReadOutboxHead(view, current.Partition).Tail
            && current.Incarnation == owner.Incarnation
            && current.SourceOwner.PhysicalShardId == owner.NodeId
            && current.PayloadDigest == (work is null ? Bm25CorpusDigest.Compute(current, placement, maximumBytes)
                : Bm25CorpusDigest.Compute(current, placement, work));
    }
}
