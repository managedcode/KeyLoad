using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.EventStreams;

internal sealed class EventVectorEndpointExecution(EventVectorRemoteReceiver receiver,
    EventVectorRemoteCallValidation authority, EventVectorRemoteEncoding encoding,
    RemoteDocumentMac originalCallerMac, RemoteDocumentMac originalReplyMac,
    ReplicaMembershipAuthorityAddressPins originalPins, ReplicaMembershipAuthorityReplayCache originalReplay,
    RemoteDocumentWorkOwner originalWork, IOptions<GrainRoutingOptions> routing, TimeProvider clock)
{
    internal async Task HandleAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var failures = new List<Exception>();
        RemoteDocumentWorkOwner.Operation? operation = null;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                operation = originalWork.Acquire(Guid.NewGuid());
                await ExecuteAsync(context, operation).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            if (operation is not null)
            { ServerFailureObserver.Observe(operation.Dispose, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task ExecuteAsync(HttpContext context, RemoteDocumentWorkOwner.Operation operation)
    {
        routing.Value.Validate();
        using var deadline = new CancellationTokenSource(routing.Value.ExecutionLifetime, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted,
            operation.ShutdownToken, deadline.Token);
        var verified = await EventVectorRemoteRequestRead.ReadAsync(context.Request,
            originalCallerMac, encoding, linked.Token).ConfigureAwait(false);
        try
        {
            var admitted = await EventVectorAdmittedCall.AdmitAsync(verified, authority,
                originalPins, originalReplay, linked.Token).ConfigureAwait(false);
            var remaining = admitted.Original.Call.ExpiresAt - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            { throw Errors.Fail(ErrorCode.TokenInvalidated, EventVectorRemoteProtocol.Unavailable); }
            using var originalExpiry = new CancellationTokenSource(remaining, clock);
            using var original = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, originalExpiry.Token);
            await EventVectorEndpointReply.WriteAsync(context, admitted, receiver, authority,
                originalReplyMac, encoding, original.Token).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(verified.OriginalRequestBytes).Span);
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(verified.Call.OriginalNativeRequest).Span);
        }
    }
}
