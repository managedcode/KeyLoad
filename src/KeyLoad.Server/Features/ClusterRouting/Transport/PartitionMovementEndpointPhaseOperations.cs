using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementEndpointPhaseOperations
{
    internal static Task HandleAsync(PartitionMovementEndpoint owner, HttpContext context)
        => owner.Runtime.RunAsync(token => PartitionMovementEndpointPhaseOperations.ExecuteAsync(owner, context, token), context.RequestAborted);

    internal static async Task ExecuteAsync(PartitionMovementEndpoint owner, HttpContext context, CancellationToken cancellationToken)
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
                    var body = await PartitionMovementWire.ReadAsync(context.Request, owner.Limits.Value.MaxBatchBytes,
                        bounded.Token).ConfigureAwait(false);
                    var verified = await owner.Admission.VerifyAsync(body,
                        PartitionMovementWire.Signature(context.Request.Headers), bounded.Token).ConfigureAwait(false);
                    await PartitionMovementEndpointPhaseOperations.ExecuteVerifiedAsync(owner, context, verified, bounded.Token).ConfigureAwait(false);
                }, failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ExecuteVerifiedAsync(PartitionMovementEndpoint owner, HttpContext context, PartitionMovementTransportRequest verified,
        CancellationToken cancellationToken)
    {
        var remaining = verified.Envelope.ExpiresAt - owner.Clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, PartitionMovementProtocol.Unavailable); }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var expiry = new CancellationTokenSource(remaining, owner.Clock);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var original = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, expiry.Token);
                await ServerFailureObserver.ObserveAsync(() => PartitionMovementEndpointPhaseOperations.ReplyAsync(owner, context, verified, original.Token),
                    failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ReplyAsync(PartitionMovementEndpoint owner, HttpContext context, PartitionMovementTransportRequest verified,
        CancellationToken cancellationToken)
    {
        GrainOperationReply terminal;
        try
        { terminal = await owner.Receiver.ExecuteAsync(verified, cancellationToken).ConfigureAwait(false); }
        catch (KeyLoadException error) when (error.Code != ErrorCode.Corruption)
        { terminal = new() { Error = error.Code, SafeDetail = PartitionMovementProtocol.Unavailable }; }
        var reply = new PartitionMovementTransportReply(verified.CommandId, verified.Envelope.Nonce,
            owner.Receiver.LocalOwner(), owner.Receiver.Discovery(), terminal,
            PartitionMoveOriginalDispatchIdentity.Digest(verified.CommandId, verified.Envelope));
        var encoded = PartitionMovementWire.Encode(reply, owner.Limits.Value.MaxBatchBytes);
        var key = Convert.FromBase64String(owner.Options.Value.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(key, owner.Limits.Value.MaxBatchBytes);
            context.Response.Headers[PartitionMovementProtocol.SignatureHeader] = mac.Sign(encoded, reply: true);
        }
        finally
        { CryptographicOperations.ZeroMemory(key); }
        context.Response.ContentType = PartitionMovementProtocol.ContentType;
        context.Response.ContentLength = encoded.Length;
        await context.Response.Body.WriteAsync(encoded, cancellationToken).ConfigureAwait(false);
    }
}
