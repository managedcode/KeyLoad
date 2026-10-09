using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementEndpointReceiverIssueOperations
{
    internal static Task HandleReceiverIssueAsync(PartitionMovementEndpoint owner, HttpContext context, bool query)
        => owner.Runtime.RunAsync(token => PartitionMovementEndpointReceiverIssueOperations.ReceiverIssueWithinLifetimeAsync(owner, context, query, token), context.RequestAborted);

    internal static async Task ReceiverIssueWithinLifetimeAsync(PartitionMovementEndpoint owner, HttpContext context, bool query, CancellationToken caller)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = new CancellationTokenSource(owner.Routing.Value.ExecutionLifetime, owner.Clock);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, deadline.Token);
                await ServerFailureObserver.ObserveAsync(() => PartitionMovementEndpointReceiverIssueOperations.AdmitReceiverIssueAsync(owner, context, query, linked.Token),
                    failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task AdmitReceiverIssueAsync(PartitionMovementEndpoint owner, HttpContext context, bool query, CancellationToken cancellationToken)
    {
        var bytes = await PartitionMovementWire.ReadReceiverIssueAsync(context.Request, query,
            owner.Limits.Value.MaxBatchBytes, cancellationToken).ConfigureAwait(false);
        var signature = PartitionMovementWire.Signature(context.Request.Headers);
        if (query)
        {
            var requested = await owner.Admission.VerifyReceiverIssueQueryAsync(bytes, signature, cancellationToken).ConfigureAwait(false);
            await PartitionMovementEndpointReceiverIssueOperations.ReceiverIssueWithinExpiryAsync(owner, requested.QueryExpiresAt,
                token => PartitionMovementEndpointReceiverIssueOperations.ReplyReceiverIssueProofAsync(owner, context, requested, token), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var requested = await owner.Admission.VerifyReceiverIssueAsync(bytes, signature, cancellationToken).ConfigureAwait(false);
            await PartitionMovementEndpointReceiverIssueOperations.ReceiverIssueWithinExpiryAsync(owner, requested.OriginalEnvelope.ExpiresAt,
                token => PartitionMovementEndpointReceiverIssueOperations.ReplyReceiverIssueAsync(owner, context, requested, bytes, signature, token), cancellationToken).ConfigureAwait(false);
        }
    }

    internal static async Task ReceiverIssueWithinExpiryAsync(PartitionMovementEndpoint owner, DateTimeOffset expiry,
        Func<CancellationToken, Task> reply, CancellationToken caller)
    {
        var remaining = expiry - owner.Clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, PartitionMovementProtocol.Unavailable); }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = new CancellationTokenSource(remaining, owner.Clock);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, deadline.Token);
                await ServerFailureObserver.ObserveAsync(() => reply(linked.Token), failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ReplyReceiverIssueAsync(PartitionMovementEndpoint owner, HttpContext context, PartitionMovementReceiverIssueRequest requested,
        ReadOnlyMemory<byte> bytes, string signature, CancellationToken cancellationToken)
    {
        GrainOperationReply actual;
        try
        {
            var work = new ReadExecutionBudget(owner.Limits, owner.Clock, cancellationToken);
            actual = await owner.Receiver.IssueReceiverAsync(requested, bytes, signature, work, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyLoadException error) when (error.Code != ErrorCode.Corruption)
        { actual = new() { Error = error.Code, SafeDetail = PartitionMovementProtocol.Unavailable }; }
        var reply = new PartitionMovementTransportReply(requested.OriginalPhaseCommandId, requested.IssuanceNonce,
            owner.Receiver.LocalOwner(), owner.Receiver.Discovery(), actual,
            PartitionMoveOriginalDispatchIdentity.Digest(requested.OriginalPhaseCommandId, requested.OriginalEnvelope));
        var encoded = PartitionMovementWire.Encode(reply, owner.Limits.Value.MaxBatchBytes);
        await PartitionMovementEndpointReceiverIssueOperations.WriteReceiverIssueReplyAsync(owner, context, encoded, PartitionMovementEndpointReceiverIssueOperations.SignReceiverIssueReply(owner, encoded), cancellationToken).ConfigureAwait(false);
    }

    internal static async Task ReplyReceiverIssueProofAsync(PartitionMovementEndpoint owner, HttpContext context, PartitionMovementReceiverIssueQuery requested,
        CancellationToken cancellationToken)
    {
        var work = new ReadExecutionBudget(owner.Limits, owner.Clock, cancellationToken);
        var witness = await owner.Receiver.ReadReceiverIssuanceProofAsync(requested, work, cancellationToken).ConfigureAwait(false);
        await PartitionMovementEndpointReceiverIssueOperations.WriteReceiverIssueReplyAsync(owner, context, witness.OriginalReplyBytes,
            witness.OriginalReplySignature, cancellationToken).ConfigureAwait(false);
    }

    internal static string SignReceiverIssueReply(PartitionMovementEndpoint owner, ReadOnlySpan<byte> bytes)
    {
        var key = Convert.FromBase64String(owner.Options.Value.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(key, owner.Limits.Value.MaxBatchBytes);
            return mac.SignReceiverIssue(bytes, source: false);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    internal static async Task WriteReceiverIssueReplyAsync(PartitionMovementEndpoint owner, HttpContext context, ReadOnlyMemory<byte> bytes,
        string signature, CancellationToken cancellationToken)
    {
        if (bytes.IsEmpty || bytes.Length > owner.Limits.Value.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.Unavailable); }
        context.Response.Headers[PartitionMovementProtocol.SignatureHeader] = signature;
        context.Response.ContentType = PartitionMovementProtocol.ContentType;
        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }
}
