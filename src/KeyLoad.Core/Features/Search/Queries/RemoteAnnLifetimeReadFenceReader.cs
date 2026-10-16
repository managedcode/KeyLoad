using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal RemoteAnnReadFenceV2 CaptureRemoteAnnLifetimeRead(string principalId, ProjectionConsumerRef consumer,
        string collection, string field, VectorSpace space, long generation,
        RegisteredPhysicalOwnerV1 configuredLocal, ReadExecutionBudget budget)
        => Store.Read(view =>
        {
            AnnReadLifetimeMetadata? metadata = null;
            var original = CaptureRemoteAnnRead(view, principalId, consumer, collection, field, space,
                generation, configuredLocal, budget, (principal, resource) =>
                {
                    var definitions = PartitionMoveResources.Capture(budget.CreateView(view), consumer.Partition, Limits);
                    var coverage = PartitionControlLifetimeCoverage.Capture(budget.CreateView(view),
                        consumer.Partition, definitions, Limits.MaxBatchBytes);
                    metadata = new(resource, coverage, principal, definitions);
                    metadata.Require(consumer.Partition, Limits.MaxBatchBytes);
                });
            var identity = Store.Identity;
            return new RemoteAnnReadFenceV2(original,
                metadata ?? throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteAnnReadInvalid),
                identity.NodeId, identity.ReadGeneration, configuredLocal);
        });

    internal void ValidateRemoteAnnLifetimeRead(RemoteAnnReadFenceV2 original, ProjectionConsumerRef consumer,
        string collection, string field, VectorSpace space, long generation, ReadExecutionBudget budget)
    {
        var current = CaptureRemoteAnnLifetimeRead(original.Metadata.Principal.Id, consumer, collection, field,
            space, generation, original.ConfiguredLocal, budget);
        if (current.SourceNodeId != original.SourceNodeId || current.SourceReadGeneration != original.SourceReadGeneration
            || !SameRemoteAnnLifetimeRoute(current.Original, original.Original)
            || RemoteAnnDigest(current.Metadata.Principal, budget) != RemoteAnnDigest(original.Metadata.Principal, budget)
            || RemoteAnnDigest(current.Metadata.Definition, budget) != RemoteAnnDigest(original.Metadata.Definition, budget)
            || RemoteAnnDigest(current.Metadata.Coverage, budget) != RemoteAnnDigest(original.Metadata.Coverage, budget)
            || RemoteAnnDigest(current.Metadata.Definitions, budget) != RemoteAnnDigest(original.Metadata.Definitions, budget))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteAnnReadInvalid);
        }
    }

    private static bool SameRemoteAnnLifetimeRoute(RemoteAnnReadFence? current, RemoteAnnReadFence? original)
    {
        if (current is null || original is null)
        {
            return current is null && original is null;
        }
        return current.PrincipalId == original.PrincipalId && current.TenantId == original.TenantId
            && current.PolicyEpoch == original.PolicyEpoch && current.SourceNodeId == original.SourceNodeId
            && current.SourceReadGeneration == original.SourceReadGeneration
            && current.DirectoryRevision == original.DirectoryRevision && current.ResourceSha256 == original.ResourceSha256
            && current.ConsumerSha256 == original.ConsumerSha256
            && PhysicalOwnerEntryValidation.Same(current.ConfiguredLocal, original.ConfiguredLocal)
            && PhysicalOwnerEntryValidation.Same(current.Destination, original.Destination);
    }
}
