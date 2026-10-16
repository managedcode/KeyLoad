using KeyLoad.Clustering;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.BlobStorage;

internal sealed class RemoteBlobLifetimeReceiver(OrleansNode node, PartitionHost partition,
    ServerRuntimeOptions options, TimeProvider clock)
{
    internal async Task<ReadOnlyMemory<byte>> ExecuteAsync(RemoteResourceLifetimeReadCallV2 call, CancellationToken token,
        IResourceLifetimeWorkBinding binding)
    {
        var request = Require(call, token);
        var principal = partition.Database.Store.Read(view => partition.Database.Principal(view,
            PartitionStoreProtocol.AdministratorId, clock.GetUtcNow()));
        GrainRequestAuthority.RequireAdministrator(principal);
        var requestId = Guid.NewGuid();
        var signed = node.CatalogRequestCodec().CreateControlledBlobReadV2(requestId, principal.Id, request);
        var actual = await NativeLifetimeReadChildExecution.ExecuteAsync<ControlledBlobReadResultV2>(
            node, principal, requestId, signed, null, token, binding).ConfigureAwait(false);
        _ = Require(call, token);
        token.ThrowIfCancellationRequested();
        if (NativeSerialization.Measure(actual) > call.MaximumReplyBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteDocumentProtocol.Unavailable);
        }
        return NativeSerialization.Serialize(actual);
    }

    private ControlledBlobReadRequestV2 Require(RemoteResourceLifetimeReadCallV2 call, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var settings = options.Node.Value.PhysicalGroupTrust;
        if (call.Kind != RemoteResourceLifetimeReadKindV2.ControlledBlob
            || call.Payload.IsEmpty || call.Payload.Length > partition.Database.Limits.MaxBatchBytes
            || !settings.RemoteDocumentReads || !settings.RegisterPhysicalOwners
            || settings.Role != PhysicalGroupTrustRole.Destination
            || call.Version != RemoteResourceLifetimeReadProtocol.Version || call.RequestId == Guid.Empty
            || !RemoteModelNativeIdentity.ValidNonce(call.Nonce)
            || call.ExpiresAt <= clock.GetUtcNow()
            || call.ExpiresAt > clock.GetUtcNow() + options.GrainRouting.Value.RequestLifetime
            || !PhysicalOwnerConfiguredTuples.Control(options.Node.Value, partition).Owner.VoterIds
                .Contains(call.CallerVoter, StringComparer.Ordinal)
            || !PhysicalGroupAddress.CanonicalAddress(call.CallerSiloAddress, options.Membership)
            || call.Source is null || call.Destination is null
            || !PhysicalOwnerEntryValidation.SameOwner(call.Source,
                PhysicalOwnerConfiguredTuples.Control(options.Node.Value, partition).Owner)
            || !PhysicalOwnerEntryValidation.Same(call.Destination,
                PhysicalOwnerConfiguredTuples.Local(options.Node.Value, partition))
            || call.MaximumReplyBytes <= RemoteControlledBlobProtocol.EmptyCount
            || call.MaximumReplyBytes > partition.Database.Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
        var request = NativeSerialization.Deserialize<ControlledBlobReadRequestV2>(call.Payload.Span);
        var frame = request?.Frame;
        var original = frame?.Original;
        if (request is null || frame is null || original is null || original.Purpose == ControlledBlobReadPurpose.Outcome
            || original.ExpiresAt != call.ExpiresAt || original.Publication is null || original.Control is null
            || !PhysicalOwnerEntryValidation.SameOwner(original.Publication.Destination, call.Destination.Owner)
            || request.MaximumResultBytes > call.MaximumReplyBytes)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
        var blob = BlobControlledReadScope.Require(original);
        if (blob.Partition != original.Control.Partition)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
        PartitionControlLifetimeCoverage.Require(frame.Coverage, blob.Partition,
            [original.Resource], partition.Database.Limits.MaxBatchBytes);
        _ = node.Discovery?.Read() ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);
        return request;
    }
}
