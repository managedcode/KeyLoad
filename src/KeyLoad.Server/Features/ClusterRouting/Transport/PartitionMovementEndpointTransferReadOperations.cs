using System.Security.Cryptography;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementEndpointTransferReadOperations
{
    internal static Task HandleTransferDataAsync(PartitionMovementEndpoint owner, HttpContext context)
        => owner.Runtime.RunAsync(token => PartitionMovementEndpointTransferReadOperations.ExecuteTransferDataAsync(owner, context, token), context.RequestAborted);

    internal static async Task ExecuteTransferDataAsync(PartitionMovementEndpoint owner, HttpContext context, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = new CancellationTokenSource(owner.Routing.Value.ExecutionLifetime, owner.Clock);
            await ServerFailureObserver.ObserveAsync(() => PartitionMovementEndpointTransferReadOperations.TransferDataWithinDeadlineAsync(owner, context,
                cancellationToken, deadline.Token), failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task TransferDataWithinDeadlineAsync(PartitionMovementEndpoint owner, HttpContext context, CancellationToken caller,
        CancellationToken deadline)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(caller, deadline);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var body = await PartitionMovementWire.ReadTransferDataAsync(context.Request,
                    owner.Limits.Value.MaxBatchBytes, bounded.Token).ConfigureAwait(false);
                var verified = await owner.Admission.VerifyTransferDataAsync(body,
                    PartitionMovementWire.Signature(context.Request.Headers), bounded.Token).ConfigureAwait(false);
                await PartitionMovementEndpointTransferReadOperations.TransferDataWithinExpiryAsync(owner, context, verified, bounded.Token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task TransferDataWithinExpiryAsync(PartitionMovementEndpoint owner, HttpContext context, PartitionMovementTransferDataRequest verified,
        CancellationToken cancellationToken)
    {
        var remaining = verified.ExpiresAt - owner.Clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, PartitionMovementProtocol.Unavailable); }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var expiry = new CancellationTokenSource(remaining, owner.Clock);
            await ServerFailureObserver.ObserveAsync(() => PartitionMovementEndpointTransferReadOperations.TransferDataWithLinkedExpiryAsync(owner, context,
                verified, cancellationToken, expiry.Token), failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task TransferDataWithLinkedExpiryAsync(PartitionMovementEndpoint owner, HttpContext context, PartitionMovementTransferDataRequest verified,
        CancellationToken caller, CancellationToken expiry)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, expiry);
            await ServerFailureObserver.ObserveAsync(() => PartitionMovementEndpointTransferReadOperations.ReplyTransferDataAsync(owner, context, verified, linked.Token),
                failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ReplyTransferDataAsync(PartitionMovementEndpoint owner, HttpContext context, PartitionMovementTransferDataRequest request,
        CancellationToken cancellationToken)
    {
        GrainOperationReply terminal;
        try
        { terminal = await owner.Receiver.ExecuteTransferDataAsync(request, cancellationToken).ConfigureAwait(false); }
        catch (KeyLoadException error) when (error.Code != ErrorCode.Corruption)
        { terminal = new() { Error = error.Code, SafeDetail = PartitionMovementProtocol.Unavailable }; }
        var reply = new PartitionMovementTransferDataReply(request.RequestId, request.Nonce,
            owner.Receiver.LocalOwner(), owner.Receiver.Discovery(), terminal);
        if (NativeSerialization.Measure(reply) > owner.Limits.Value.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMovementProtocol.Unavailable); }
        var encoded = NativeSerialization.Serialize(reply);
        var key = Convert.FromBase64String(owner.Options.Value.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(key, owner.Limits.Value.MaxBatchBytes);
            context.Response.Headers[PartitionMovementProtocol.SignatureHeader] = mac.SignTransferData(encoded, reply: true);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        context.Response.ContentType = PartitionMovementProtocol.ContentType;
        context.Response.ContentLength = encoded.Length;
        await context.Response.Body.WriteAsync(encoded, cancellationToken).ConfigureAwait(false);
    }
}
