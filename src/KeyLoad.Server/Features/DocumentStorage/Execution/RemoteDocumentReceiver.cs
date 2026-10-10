using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.QueryExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteDocumentReceiver(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> nodeOptions, IOptions<GrainRoutingOptions> routingOptions,
    IOptions<OrleansMembershipOptions> membershipOptions, TimeProvider clock)
{
    internal ReplicaSiloDiscovery Discovery() => node.Discovery?.Read()
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);

    internal void Validate(RemoteDocumentCallV1 call, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = nodeOptions.Value.MembershipAuthority;
        var now = clock.GetUtcNow();
        if (!settings.RemoteDocumentReads || !settings.RegisterPhysicalOwners
            || (call.QueryLeaf is not null || call.SearchLeaf is not null) && !settings.RemotePartitionQueries
            || settings.Mode != MembershipAuthoritySettingsProtocol.Proxy
            || call.Version != RemoteDocumentProtocol.Version || call.RequestId == Guid.Empty
            || !ReplicaMembershipAuthorityValidation.ValidNonce(call.Nonce)
            || !settings.AuthorityEndpoints.Contains(call.CallerVoter, StringComparer.Ordinal)
            || !ReplicaMembershipAuthorityValidation.CanonicalAddress(call.CallerSiloAddress, membershipOptions)
            || call.ExpiresAt <= now || call.ExpiresAt > now + routingOptions.Value.RequestLifetime
            || call.Source is null || call.Fence is null || !RemotePartitionQueryScope.Valid(call)
            || !PhysicalOwnerEntryValidation.SameOwner(call.Source,
                PhysicalOwnerConfiguredTuples.Control(nodeOptions.Value, partition).Owner)
            || !PhysicalOwnerEntryValidation.Same(call.Fence.Destination,
                PhysicalOwnerConfiguredTuples.Local(nodeOptions.Value, partition))
            || call.Fence.Placement.IsFallback || call.Fence.Placement.Partition != RemotePartitionQueryScope.Partition(call)
            || call.Fence.Placement.PhysicalShardId != nodeOptions.Value.PhysicalShardId
            || call.Fence.Placement.Incarnation != call.Fence.Destination.Owner.Incarnation
            || call.Fence.Placement.PlacementEpoch != call.Fence.Destination.Owner.PlacementEpoch
            || !call.Fence.Placement.VoterIds.SequenceEqual(call.Fence.Destination.Owner.VoterIds, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
    }

    internal async Task<OwnedDocumentReadResultV1> ReadAsync(RemoteDocumentCallV1 call,
        CancellationToken cancellationToken)
    {
        Validate(call, cancellationToken);
        var principal = partition.Database.Store.Read(view => partition.Database.Principal(view,
            call.Fence.PrincipalId, clock.GetUtcNow()));
        if (!string.Equals(principal.TenantId, call.Fence.Tenant, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.PermissionDenied, RemoteDocumentProtocol.InvalidProof); }
        var requestId = Guid.NewGuid();
        var request = new OwnedDocumentReadRequestV1(call.Fence.Tenant, call.Request!, call.Fence.Destination.Owner);
        var signed = node.CatalogRequestCodec().CreateOwnedDocumentRead(requestId, principal.Id,
            NativeSerialization.Serialize(request), call.ExpiresAt);
        using var identity = node.OpenRequestContext(principal, requestId, Guid.Empty, cancellationToken);
        var reply = await node.ExecuteAsync(requestId, signed, command: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Validate(call, cancellationToken);
        var result = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as OwnedDocumentReadResultV1
            ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
        var actual = partition.Database.Store.Identity;
        if (result.NodeId != actual.NodeId || result.Incarnation != actual.Incarnation
            || result.ReadGeneration != actual.ReadGeneration)
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable); }
        return result;
    }
    internal async Task<PartitionQueryLeafResultV1> ReadLeafAsync(RemoteDocumentCallV1 call,
        CancellationToken cancellationToken)
    {
        Validate(call, cancellationToken);
        var principal = partition.Database.Store.Read(view => partition.Database.Principal(view,
            call.Fence.PrincipalId, clock.GetUtcNow()));
        if (principal.TenantId != call.Fence.Tenant)
        { throw Errors.Fail(ErrorCode.PermissionDenied, RemoteDocumentProtocol.InvalidProof); }
        var requestId = Guid.NewGuid();
        var request = call.QueryLeaf ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
        var signed = node.CatalogRequestCodec().CreatePartitionQueryLeaf(requestId, principal.Id,
            NativeSerialization.Serialize(request), call.ExpiresAt);
        using var identity = node.OpenRequestContext(principal, requestId, Guid.Empty, cancellationToken);
        var reply = await node.ExecuteAsync(requestId, signed, command: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Validate(call, cancellationToken);
        var result = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as PartitionQueryLeafResultV1
            ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
        var actual = partition.Database.Store.Identity;
        if (result.NodeId != actual.NodeId || result.Incarnation != actual.Incarnation
            || result.ReadGeneration != actual.ReadGeneration)
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable); }
        return result;
    }

    internal async Task<KeyLoad.Query.Features.QueryExecution.DistributedSearchLeafResultV1> ReadSearchLeafAsync(
        RemoteDocumentCallV1 call, CancellationToken cancellationToken)
    {
        Validate(call, cancellationToken);
        var principal = partition.Database.Store.Read(view => partition.Database.Principal(view,
            call.Fence.PrincipalId, clock.GetUtcNow()));
        if (principal.TenantId != call.Fence.Tenant)
        { throw Errors.Fail(ErrorCode.PermissionDenied, RemoteDocumentProtocol.InvalidProof); }
        var requestId = Guid.NewGuid();
        var request = call.SearchLeaf ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
        var signed = node.CatalogRequestCodec().CreateDistributedSearchLeaf(requestId, principal.Id,
            NativeSerialization.Serialize(request), call.ExpiresAt);
        using var identity = node.OpenRequestContext(principal, requestId, Guid.Empty, cancellationToken);
        var reply = await node.ExecuteAsync(requestId, signed, command: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Validate(call, cancellationToken);
        var result = GrainNativePayload.Read<GrainValue>(reply.Payload).Value
            as KeyLoad.Query.Features.QueryExecution.DistributedSearchLeafResultV1
            ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
        var actual = partition.Database.Store.Identity;
        if (result.Witness is null || result.Witness.NodeId != actual.NodeId
            || result.Witness.Incarnation != actual.Incarnation || result.Witness.ReadGeneration != actual.ReadGeneration)
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable); }
        return result;
    }
}
