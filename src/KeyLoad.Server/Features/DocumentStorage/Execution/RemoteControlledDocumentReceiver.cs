using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteControlledDocumentReceiver(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> options, IOptions<OrleansMembershipOptions> membership,
    IOptions<GrainRoutingOptions> routing, TimeProvider clock)
{
    internal ReplicaSiloDiscovery Discovery() => node.Discovery?.Read()
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);

    internal void Validate(RemoteControlledDocumentCall call, CancellationToken token)
        => RemoteControlledDocumentValidation.Require(node, partition, options, membership, routing, clock, call, token);

    internal async Task<ControlledDocumentReadResult> ReadAsync(RemoteControlledDocumentCall call,
        CancellationToken token)
    {
        Validate(call, token);
        var principal = partition.Database.Store.Read(view => partition.Database.Principal(view,
            PartitionStoreProtocol.AdministratorId, clock.GetUtcNow()));
        GrainRequestAuthority.RequireAdministrator(principal);
        var requestId = Guid.NewGuid();
        var signed = node.CatalogRequestCodec().CreateControlledDocumentRead(requestId, principal.Id, call.Request);
        ControlledDocumentReadResult? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var identity = node.OpenRequestContext(principal, requestId, Guid.Empty, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var reply = await node.ExecuteAsync(requestId, signed, command: false, cancellationToken: token).ConfigureAwait(false);
                Validate(call, token);
                if (reply.Error is { } error)
                { throw Errors.Fail(error, reply.SafeDetail ?? RemoteDocumentProtocol.Unavailable); }
                result = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as ControlledDocumentReadResult
                    ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
    }
}
