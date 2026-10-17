using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.QueryExecution;

internal static class RemoteQueryLifetimeScope
{
    internal static void Require(RemoteResourceLifetimeReadCallV2 call, RemoteQueryLifetimeRequestV2 request,
        RemoteDocumentReceiver receiver, DatabaseLimits limits, CancellationToken token)
    {
        if (request is null || request.Original is not { } original || request.Definition is null
            || request.Coverage is null || request.Principal is null || original.Fence is null
            || request.Principal.Id != original.Fence.PrincipalId
            || request.Principal.TenantId != original.Fence.Tenant
            || request.Principal.PolicyEpoch != original.Fence.PolicyEpoch
            || original.RequestId != call.RequestId || original.Nonce != call.Nonce
            || original.ExpiresAt != call.ExpiresAt || original.CallerVoter != call.CallerVoter
            || original.CallerSiloAddress != call.CallerSiloAddress
            || !PhysicalOwnerEntryValidation.SameOwner(original.Source, call.Source)
            || !PhysicalOwnerEntryValidation.Same(original.Fence.Destination, call.Destination)
            || call.Kind == RemoteResourceLifetimeReadKindV2.PartitionQuery
                && (original.QueryLeaf is null || original.SearchLeaf is not null || original.Request is not null)
            || call.Kind == RemoteResourceLifetimeReadKindV2.DistributedSearch
                && (original.SearchLeaf is null || original.QueryLeaf is not null || original.Request is not null)
            || call.Kind is not (RemoteResourceLifetimeReadKindV2.PartitionQuery
                or RemoteResourceLifetimeReadKindV2.DistributedSearch))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
        receiver.Validate(original, token);
        var partition = RemotePartitionQueryScope.Partition(original);
        var collection = original.QueryLeaf?.Plan.Request.Query.Collection ?? original.SearchLeaf!.Search.Collection;
        if (request.Definition.Name != collection)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.InvalidProof);
        }
        PartitionControlLifetimeCoverage.Require(request.Coverage, partition, [request.Definition], limits.MaxBatchBytes);
    }
}
