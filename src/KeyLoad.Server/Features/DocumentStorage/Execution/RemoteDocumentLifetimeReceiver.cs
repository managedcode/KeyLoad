using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteDocumentLifetimeReceiver(OrleansNode node, PartitionHost partition,
    ServerRuntimeOptions options, TimeProvider clock)
{
    internal Task<ReadOnlyMemory<byte>> ExecuteAsync(RemoteResourceLifetimeReadCallV2 call, CancellationToken token,
        IResourceLifetimeWorkBinding binding)
    {
        token.ThrowIfCancellationRequested();
        if (!options.Node.Value.PhysicalGroupTrust.RemoteDocumentReads)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteDocumentProtocol.Unavailable);
        }
        return call.Kind switch
        {
            RemoteResourceLifetimeReadKindV2.Document => OwnedAsync(call, token, binding),
            RemoteResourceLifetimeReadKindV2.ControlledDocument => ControlledAsync(call, token, binding),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteDocumentProtocol.InvalidProof)
        };
    }

    private async Task<ReadOnlyMemory<byte>> OwnedAsync(RemoteResourceLifetimeReadCallV2 call, CancellationToken token,
        IResourceLifetimeWorkBinding binding)
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
        var result = await NativeLifetimeReadChildExecution.ExecuteAsync<OwnedDocumentReadResultV2>(node,
            principal, id, signed, null, token, binding).ConfigureAwait(false);
        var actual = partition.Database.Store.Identity;
        if (result.Result.NodeId != actual.NodeId || result.Result.Incarnation != actual.Incarnation
            || result.Result.ReadGeneration != actual.ReadGeneration)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);
        }
        token.ThrowIfCancellationRequested();
        return Encode(result, call.MaximumReplyBytes);
    }

    private async Task<ReadOnlyMemory<byte>> ControlledAsync(RemoteResourceLifetimeReadCallV2 call, CancellationToken token,
        IResourceLifetimeWorkBinding binding)
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
        var result = await NativeLifetimeReadChildExecution.ExecuteAsync<ControlledDocumentReadResultV2>(node,
            principal, id, signed, null, token, binding).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return Encode(result, call.MaximumReplyBytes);
    }

    private static byte[] Encode<T>(T result, int maximumBytes)
    {
        if (NativeSerialization.Measure(result) > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteDocumentProtocol.InvalidProof);
        }
        return NativeSerialization.Serialize(result);
    }
}
