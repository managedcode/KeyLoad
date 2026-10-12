using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.EventStreams;

internal sealed class EventVectorEndpointExecution(EventVectorRemoteReceiver receiver,
    EventVectorRemoteCallValidation authority, EventVectorRemoteEncoding encoding,
    RemoteDocumentMac originalCallerMac, RemoteDocumentMac originalReplyMac,
    EventVectorReplayCache originalReplay, RemoteDocumentWorkOwner originalWork,
    IOptions<GrainRoutingOptions> routing, TimeProvider clock)
{
    private const int NoCleanupFailures = 0;
    internal Task<(ReadOnlyMemory<byte> Payload, string Signature)> ExecuteAsync(
        ReadOnlyMemory<byte> originalBytes, string originalSignature, CancellationToken originalCancellationToken)
        => ExecuteAsync(originalBytes, originalSignature, null, originalCancellationToken);

    internal async Task<(ReadOnlyMemory<byte> Payload, string Signature)> ExecuteAsync(
        ReadOnlyMemory<byte> originalBytes, string originalSignature, RemoteModelNativeEffectBoundary? boundary, CancellationToken originalCancellationToken)
    {
        var failures = new List<Exception>();
        RemoteDocumentWorkOwner.Operation? operation = null;
        (ReadOnlyMemory<byte> Payload, string Signature)? result = null;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                operation = originalWork.Acquire(Guid.NewGuid());
                result = await ExecuteOwnedAsync(originalBytes, originalSignature, operation, boundary,
                    originalCancellationToken).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            if (operation is not null)
            { ServerFailureObserver.Observe(operation.Dispose, failures); }
        }
        if (failures.Count != NoCleanupFailures && result is { } abandoned)
        { CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(abandoned.Payload).Span); }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid);
    }

    private async Task<(ReadOnlyMemory<byte> Payload, string Signature)> ExecuteOwnedAsync(
        ReadOnlyMemory<byte> originalBytes, string originalSignature,
        RemoteDocumentWorkOwner.Operation operation, RemoteModelNativeEffectBoundary? boundary,
        CancellationToken originalCancellationToken)
    {
        routing.Value.Validate();
        using var deadline = new CancellationTokenSource(routing.Value.ExecutionLifetime, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(originalCancellationToken,
            operation.ShutdownToken, deadline.Token);
        var verified = EventVectorRemoteRequestRead.Read(originalBytes, originalSignature,
            originalCallerMac, encoding, linked.Token);
        try
        {
            var admitted = EventVectorAdmittedCall.Admit(verified, authority, originalReplay, linked.Token);
            var remaining = admitted.Original.Call.ExpiresAt - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            { throw Errors.Fail(ErrorCode.TokenInvalidated, EventVectorRemoteProtocol.Unavailable); }
            using var expiry = new CancellationTokenSource(remaining, clock);
            using var original = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, expiry.Token);
            return await EventVectorEndpointReply.CreateAsync(admitted, receiver, authority,
                originalReplyMac, encoding, boundary, original.Token).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(verified.OriginalRequestBytes).Span);
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(verified.Call.OriginalNativeRequest).Span);
        }
    }
}
