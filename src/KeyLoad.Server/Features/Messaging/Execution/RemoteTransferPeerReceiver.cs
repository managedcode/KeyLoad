using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Messaging;

internal sealed class RemoteTransferPeerReceiver(OrleansNode node, PartitionHost partition,
    IOptions<GrainRoutingOptions> routing, TimeProvider clock)
{
    internal ReplicaSiloDiscovery Discovery() => node.Discovery?.Read()
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Unavailable);

    internal async Task<RemoteQueueTransferPeerResult> ExecuteAsync(RemoteQueueTransferPeerCall call,
        ReadOnlyMemory<byte> envelope, string signature, CancellationToken token)
    {
        var database = partition.Database;
        var work = new ReadExecutionBudget(database.OperationLimitsOptions, clock, token);
        work.ConstrainLifetime(call.ExpiresAt);
        var admitted = AdmittedRemoteTransferCall.Admit(database, envelope, signature, call, work);
        var proof = admitted.Proof;
        var principal = database.Store.Read(view => database.Principal(work.CreateView(view),
            proof.TechnicalPrincipalId, clock.GetUtcNow()));
        var requestId = call.RequestId;
        var command = call.Stage == RemoteQueueTransferPeerStage.Accept;
        var original = command ? database.CreateRemoteTransferNativeOperation(admitted, work) : null;
        var codec = node.CatalogRequestCodec();
        var envelopeRequest = original is not null
            ? RemoteTransferRequestEnvelopes.Command(database, clock.GetUtcNow(),
                routing.Value.RequestLifetime, requestId, original, call.ExpiresAt)
            : RemoteTransferRequestEnvelopes.Read(database, clock.GetUtcNow(),
                routing.Value.RequestLifetime, requestId, proof);
        var signed = codec.Issue(envelopeRequest);
        RemoteQueueTransferPeerResult? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var identity = node.OpenRequestContext(principal, requestId,
                command ? call.OriginalCommandId : Guid.Empty, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var reply = await node.ExecuteAsync(requestId, signed, command, token).ConfigureAwait(false);
                work.Check();
                result = reply.Error is { } error
                    ? ObserveRejectedReply(database, admitted, work, error, reply.SafeDetail)
                    : database.ObserveRemoteTransferPeer(admitted, work);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid);
    }

    private static RemoteQueueTransferPeerResult ObserveRejectedReply(DatabaseEngine database,
        AdmittedRemoteTransferCall admitted, ReadExecutionBudget work, ErrorCode error, string? detail)
    {
        var primary = Errors.Fail(error, detail ?? RemoteTransferPeerProtocol.Unavailable);
        RemoteQueueTransferPeerResult? observed = null;
        var failures = new List<Exception> { primary };
        ServerFailureObserver.Observe(() => observed = database.ObserveRemoteTransferPeer(admitted, work), failures);
        if (observed?.OriginalOutcome?.Result is { } original
            && original.Error == error && original.SafeDetail == detail)
        { return observed; }
        ServerFailureObserver.ThrowIfAny(failures);
        throw primary;
    }
}
