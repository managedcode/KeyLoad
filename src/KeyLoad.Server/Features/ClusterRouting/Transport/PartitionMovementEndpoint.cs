using System.Security.Cryptography;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PartitionMovementEndpoint(PartitionMovementRuntime runtime,
    PartitionMovementPeerAdmission admission, PartitionMovementReceiver receiver,
    IOptions<NodeOptions> options, IOptions<GrainRoutingOptions> routing, IOptions<DatabaseLimits> limits, TimeProvider clock)
{
    internal Task HandleAsync(HttpContext context)
        => runtime.RunAsync(token => ExecuteAsync(context, token), context.RequestAborted);

    private async Task ExecuteAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = new CancellationTokenSource(routing.Value.ExecutionLifetime, clock);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var body = await PartitionMovementWire.ReadAsync(context.Request, limits.Value.MaxBatchBytes,
                        bounded.Token).ConfigureAwait(false);
                    var verified = await admission.VerifyAsync(body,
                        PartitionMovementWire.Signature(context.Request.Headers), bounded.Token).ConfigureAwait(false);
                    await ExecuteVerifiedAsync(context, verified, bounded.Token).ConfigureAwait(false);
                }, failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task ExecuteVerifiedAsync(HttpContext context, PartitionMovementTransportRequest verified,
        CancellationToken cancellationToken)
    {
        var remaining = verified.Envelope.ExpiresAt - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, PartitionMovementProtocol.Unavailable); }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var expiry = new CancellationTokenSource(remaining, clock);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var original = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, expiry.Token);
                await ServerFailureObserver.ObserveAsync(() => ReplyAsync(context, verified, original.Token),
                    failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task ReplyAsync(HttpContext context, PartitionMovementTransportRequest verified,
        CancellationToken cancellationToken)
    {
        GrainOperationReply terminal;
        try
        { terminal = await receiver.ExecuteAsync(verified, cancellationToken).ConfigureAwait(false); }
        catch (KeyLoadException error) when (error.Code != ErrorCode.Corruption)
        { terminal = new() { Error = error.Code, SafeDetail = PartitionMovementProtocol.Unavailable }; }
        var reply = new PartitionMovementTransportReply(verified.CommandId, verified.Envelope.Nonce,
            receiver.LocalOwner(), receiver.Discovery(), terminal);
        var encoded = PartitionMovementWire.Encode(reply, limits.Value.MaxBatchBytes);
        var key = Convert.FromBase64String(options.Value.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(key, limits.Value.MaxBatchBytes);
            context.Response.Headers[PartitionMovementProtocol.SignatureHeader] = mac.Sign(encoded, reply: true);
        }
        finally
        { CryptographicOperations.ZeroMemory(key); }
        context.Response.ContentType = PartitionMovementProtocol.ContentType;
        context.Response.ContentLength = encoded.Length;
        await context.Response.Body.WriteAsync(encoded, cancellationToken).ConfigureAwait(false);
    }
}
