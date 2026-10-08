using System.Security.Cryptography;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementEndpoint
{
    internal Task HandleOutcomeAsync(HttpContext context)
        => runtime.RunAsync(token => ExecuteOutcomeAsync(context, token), context.RequestAborted);

    private async Task ExecuteOutcomeAsync(HttpContext context, CancellationToken cancellationToken)
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
                    var body = await PartitionMovementWire.ReadOutcomeAsync(context.Request, limits.Value.MaxBatchBytes,
                        bounded.Token).ConfigureAwait(false);
                    var query = await admission.VerifyOutcomeAsync(body,
                        PartitionMovementWire.Signature(context.Request.Headers), bounded.Token).ConfigureAwait(false);
                    await ExecuteOutcomeVerifiedAsync(context, query, bounded.Token).ConfigureAwait(false);
                }, failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task ExecuteOutcomeVerifiedAsync(HttpContext context, PartitionMovementOutcomeTransportRequest query,
        CancellationToken cancellationToken)
    {
        var remaining = query.ExpiresAt - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, PartitionMovementProtocol.Unavailable); }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var expiry = new CancellationTokenSource(remaining, clock);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var original = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, expiry.Token);
                await ServerFailureObserver.ObserveAsync(() => ReplyOutcomeAsync(context, query, original.Token),
                    failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task ReplyOutcomeAsync(HttpContext context, PartitionMovementOutcomeTransportRequest query,
        CancellationToken cancellationToken)
    {
        GrainOperationReply terminal;
        try
        { terminal = await receiver.ExecuteOutcomeAsync(query, cancellationToken).ConfigureAwait(false); }
        catch (KeyLoadException error) when (error.Code != ErrorCode.Corruption)
        {
            terminal = new()
            {
                Error = error.Code,
                SafeDetail = error.Code == ErrorCode.RecoveryRequired
            && error.Message == ControlledDocumentFailureProtocol.MissingDurableOutcome
                ? ControlledDocumentFailureProtocol.MissingDurableOutcome : PartitionMovementProtocol.Unavailable
            };
        }
        var reply = new PartitionMovementTransportReply(query.Original.CommandId, query.Nonce,
            receiver.LocalOwner(), receiver.Discovery(), terminal);
        var encoded = PartitionMovementWire.Encode(reply, limits.Value.MaxBatchBytes);
        var key = Convert.FromBase64String(options.Value.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(key, limits.Value.MaxBatchBytes);
            context.Response.Headers[PartitionMovementProtocol.SignatureHeader] = mac.SignOutcome(encoded, reply: true);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        context.Response.ContentType = PartitionMovementProtocol.ContentType;
        context.Response.ContentLength = encoded.Length;
        await context.Response.Body.WriteAsync(encoded, cancellationToken).ConfigureAwait(false);
    }
}
