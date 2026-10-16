using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.DocumentStorage;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteDocumentLifetimeReceiver(OrleansNode node, PartitionHost partition,
    ServerRuntimeOptions options, TimeProvider clock)
{
    internal Task<ReadOnlyMemory<byte>> ExecuteAsync(RemoteResourceLifetimeReadCallV2 call, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!options.Node.Value.PhysicalGroupTrust.RemoteDocumentReads)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteDocumentProtocol.Unavailable);
        }
        return call.Kind switch
        {
            RemoteResourceLifetimeReadKindV2.Document => OwnedAsync(call, token),
            RemoteResourceLifetimeReadKindV2.ControlledDocument => ControlledAsync(call, token),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteDocumentProtocol.InvalidProof)
        };
    }

    private async Task<ReadOnlyMemory<byte>> OwnedAsync(RemoteResourceLifetimeReadCallV2 call, CancellationToken token)
    {
        var request = NativeSerialization.Deserialize<OwnedDocumentReadRequestV2>(call.Payload.Span);
        var frame = request.Frame;
        if (frame is null || frame.Principal is null || frame.Route is null || frame.Reference is null || request.Request is null
            || request.Request.Reference is null || frame.Resource is null || frame.Coverage is null
            || frame.ExpiresAt != call.ExpiresAt || frame.Reference != request.Request.Reference
            || !PhysicalOwnerEntryValidation.Same(frame.Route.Destination, call.Destination)
            || !PhysicalOwnerEntryValidation.SameOwner(request.Owner, call.Destination.Owner)
            || request.MaximumResultBytes > call.MaximumReplyBytes)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
        var principal = partition.Database.Store.Read(view => partition.Database.Principal(view, frame.Principal.Id, clock.GetUtcNow()));
        var id = Guid.NewGuid();
        var signed = node.CatalogRequestCodec().CreateOwnedDocumentReadV2(id, principal.Id, request);
        using var identity = node.OpenRequestContext(principal, id, Guid.Empty, token);
        var reply = await node.ExecuteAsync(id, signed, command: false, cancellationToken: token).ConfigureAwait(false);
        var result = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as OwnedDocumentReadResultV2
            ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
        var actual = partition.Database.Store.Identity;
        if (result.Result.NodeId != actual.NodeId || result.Result.Incarnation != actual.Incarnation
            || result.Result.ReadGeneration != actual.ReadGeneration)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);
        }
        token.ThrowIfCancellationRequested();
        return NativeSerialization.Serialize(result);
    }

    private async Task<ReadOnlyMemory<byte>> ControlledAsync(RemoteResourceLifetimeReadCallV2 call, CancellationToken token)
    {
        var request = NativeSerialization.Deserialize<ControlledDocumentReadRequestV2>(call.Payload.Span);
        var frame = request.Frame?.Original;
        if (frame is null || frame.ExpiresAt != call.ExpiresAt || frame.Publication is null || frame.Reference is null || frame.Control is null
            || !PhysicalOwnerEntryValidation.SameOwner(frame.Publication.Destination, call.Destination.Owner)
            || frame.Control.Partition != frame.Reference.Partition || request.MaximumResultBytes > call.MaximumReplyBytes)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
        var principal = partition.Database.Store.Read(view => partition.Database.Principal(view, PartitionStoreProtocol.AdministratorId, clock.GetUtcNow()));
        GrainRequestAuthority.RequireAdministrator(principal);
        var id = Guid.NewGuid();
        var signed = node.CatalogRequestCodec().CreateControlledDocumentReadV2(id, principal.Id, request);
        using var identity = node.OpenRequestContext(principal, id, Guid.Empty, token);
        var reply = await node.ExecuteAsync(id, signed, command: false, cancellationToken: token).ConfigureAwait(false);
        var result = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as ControlledDocumentReadResultV2
            ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
        token.ThrowIfCancellationRequested();
        return NativeSerialization.Serialize(result);
    }
}
