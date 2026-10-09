using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementEndpointOutcomeOperations
{
    internal static Task HandleOutcomeAsync(PartitionMovementEndpoint owner, HttpContext context)
        => owner.Runtime.RunAsync(token => PartitionMovementEndpointOutcomeOperations.ExecuteOutcomeAsync(owner, context, token), context.RequestAborted);

    internal static async Task ExecuteOutcomeAsync(PartitionMovementEndpoint owner, HttpContext context, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = new CancellationTokenSource(owner.Routing.Value.ExecutionLifetime, owner.Clock);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var body = await PartitionMovementWire.ReadOutcomeAsync(context.Request, owner.Limits.Value.MaxBatchBytes,
                        bounded.Token).ConfigureAwait(false);
                    var query = await owner.Admission.VerifyOutcomeAsync(body,
                        PartitionMovementWire.Signature(context.Request.Headers), bounded.Token).ConfigureAwait(false);
                    await PartitionMovementEndpointOutcomeOperations.ExecuteOutcomeVerifiedAsync(owner, context, query, bounded.Token).ConfigureAwait(false);
                }, failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ExecuteOutcomeVerifiedAsync(PartitionMovementEndpoint owner, HttpContext context, PartitionMovementOutcomeTransportRequest query,
        CancellationToken cancellationToken)
    {
        var remaining = query.ExpiresAt - owner.Clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, PartitionMovementProtocol.Unavailable); }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var expiry = new CancellationTokenSource(remaining, owner.Clock);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var original = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, expiry.Token);
                await ServerFailureObserver.ObserveAsync(() => PartitionMovementEndpointOutcomeOperations.ReplyOutcomeAsync(owner, context, query, original.Token),
                    failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ReplyOutcomeAsync(PartitionMovementEndpoint owner, HttpContext context, PartitionMovementOutcomeTransportRequest query,
        CancellationToken cancellationToken)
    {
        GrainOperationReply terminal;
        try
        { terminal = await owner.Receiver.ExecuteOutcomeAsync(query, cancellationToken).ConfigureAwait(false); }
        catch (KeyLoadException error) when (error.Code != ErrorCode.Corruption)
        {
            terminal = new()
            {
                Error = error.Code,
                SafeDetail = PartitionMovementProtocol.Unavailable
            };
        }
        if (terminal.Error is not null)
        { terminal = terminal with { SafeDetail = PartitionMovementProtocol.Unavailable }; }
        PartitionMovementTransportReplyValidation.RequireValue(terminal);
        var reply = new PartitionMovementTransportReply(query.Original.CommandId, query.Nonce,
            owner.Receiver.LocalOwner(), owner.Receiver.Discovery(), terminal,
            PartitionMoveOriginalDispatchIdentity.Digest(query.Original.CommandId, query.Original.Envelope));
        var encoded = PartitionMovementWire.Encode(reply, owner.Limits.Value.MaxBatchBytes);
        var key = Convert.FromBase64String(owner.Options.Value.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(key, owner.Limits.Value.MaxBatchBytes);
            context.Response.Headers[PartitionMovementProtocol.SignatureHeader] = mac.SignOutcome(encoded, reply: true);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        context.Response.ContentType = PartitionMovementProtocol.ContentType;
        context.Response.ContentLength = encoded.Length;
        await context.Response.Body.WriteAsync(encoded, cancellationToken).ConfigureAwait(false);
    }
}
