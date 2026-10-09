using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementEndpointRetireCancellationOperations
{
    internal static Task HandleRetireCancellationAsync(PartitionMovementEndpoint owner, HttpContext context, bool query)
        => owner.Runtime.RunAsync(token => PartitionMovementEndpointRetireCancellationOperations.RetireCancellationWithinLifetimeAsync(owner, context, query, token), context.RequestAborted);

    internal static async Task RetireCancellationWithinLifetimeAsync(PartitionMovementEndpoint owner, HttpContext context, bool query, CancellationToken caller)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = new CancellationTokenSource(owner.Routing.Value.ExecutionLifetime, owner.Clock);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, deadline.Token);
                await ServerFailureObserver.ObserveAsync(() => PartitionMovementEndpointRetireCancellationOperations.AdmitRetireCancellationAsync(owner, context, query, linked.Token),
                    failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task AdmitRetireCancellationAsync(PartitionMovementEndpoint owner, HttpContext context, bool query, CancellationToken cancellationToken)
    {
        var bytes = await PartitionMovementWire.ReadRetireCancellationAsync(context.Request, query,
            owner.Limits.Value.MaxBatchBytes, cancellationToken).ConfigureAwait(false);
        var signature = PartitionMovementWire.Signature(context.Request.Headers);
        if (query)
        {
            var requested = await owner.Admission.VerifyRetireCancellationQueryAsync(bytes, signature, cancellationToken).ConfigureAwait(false);
            await PartitionMovementEndpointReceiverIssueOperations.ReceiverIssueWithinExpiryAsync(owner, requested.QueryExpiresAt,
                token => PartitionMovementEndpointRetireCancellationOperations.ReplyRetireCancellationQueryAsync(owner, context, requested, bytes, signature, token), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var requested = await owner.Admission.VerifyRetireCancellationAsync(bytes, signature, cancellationToken).ConfigureAwait(false);
            await PartitionMovementEndpointReceiverIssueOperations.ReceiverIssueWithinExpiryAsync(owner, requested.CancellationExpiresAt,
                token => PartitionMovementEndpointRetireCancellationOperations.ReplyRetireCancellationAsync(owner, context, requested, bytes, signature, token), cancellationToken).ConfigureAwait(false);
        }
    }

    internal static async Task ReplyRetireCancellationAsync(PartitionMovementEndpoint owner, HttpContext context, PartitionMovementRetireCancellationRequest requested,
        ReadOnlyMemory<byte> bytes, string signature, CancellationToken cancellationToken)
    {
        GrainOperationReply actual;
        try
        {
            var work = new ReadExecutionBudget(owner.Limits, owner.Clock, cancellationToken);
            actual = await owner.Receiver.CancelExpiredRetireAsync(requested, bytes, signature, work, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyLoadException error) when (error.Code != ErrorCode.Corruption)
        { actual = new() { Error = error.Code, SafeDetail = PartitionMovementProtocol.Unavailable }; }
        var reply = new PartitionMovementTransportReply(requested.CancellationCommandId, requested.CancellationNonce,
            owner.Receiver.LocalOwner(), owner.Receiver.Discovery(), actual,
            PartitionMoveOriginalDispatchIdentity.Digest(requested.OriginalPhaseCommandId, requested.OriginalEnvelope));
        await PartitionMovementEndpointRetireCancellationOperations.WriteRetireCancellationReplyAsync(owner, context, reply, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task ReplyRetireCancellationQueryAsync(PartitionMovementEndpoint owner, HttpContext context, PartitionMovementRetireCancellationQuery requested,
        ReadOnlyMemory<byte> bytes, string signature, CancellationToken cancellationToken)
    {
        var work = new ReadExecutionBudget(owner.Limits, owner.Clock, cancellationToken);
        var reply = await owner.Receiver.ReadRetireCancellationAsync(requested, bytes, signature, work, cancellationToken).ConfigureAwait(false);
        await PartitionMovementEndpointRetireCancellationOperations.WriteRetireCancellationReplyAsync(owner, context, reply, cancellationToken).ConfigureAwait(false);
    }

    internal static Task WriteRetireCancellationReplyAsync(PartitionMovementEndpoint owner, HttpContext context, PartitionMovementTransportReply reply,
        CancellationToken cancellationToken)
    {
        var encoded = PartitionMovementWire.Encode(reply, owner.Limits.Value.MaxBatchBytes);
        return PartitionMovementEndpointReceiverIssueOperations.WriteReceiverIssueReplyAsync(owner, context, encoded, owner.Receiver.SignRetireCancellationReply(encoded), cancellationToken);
    }
}
