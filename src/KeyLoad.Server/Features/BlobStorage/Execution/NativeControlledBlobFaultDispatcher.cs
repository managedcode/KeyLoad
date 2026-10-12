using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.BlobStorage;

internal sealed class NativeControlledBlobFaultDispatcher(RemoteModelNativeClient client,
    RegisteredPhysicalOwnerV1 destination, SiloAddress actualAddress, RemoteModelNativeRequest original,
    RemoteDocumentMac borrowedSourceMac, long maximumReplyBytes, CancellationToken originalToken)
{
    internal async Task<NativeControlledBlobFaultResult> ExecuteAsync(NativeControlledBlobFaultPayload fault, int ordinal)
    {
        originalToken.ThrowIfCancellationRequested();
        await client.RequireBeforeDispatchAsync(destination, original, borrowedSourceMac, originalToken).ConfigureAwait(false);
        if (!string.Equals(fault.Signature, original.Signature, StringComparison.Ordinal)
            || !borrowedSourceMac.Verify(original.Payload.Span, original.Signature, reply: false)
            || fault.Payload.IsEmpty || fault.Payload.Length > RemoteDocumentProtocol.MaximumBodyBytes)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof); }
        var request = original with { Payload = fault.Payload, Nonce = Guid.NewGuid(), SenderSignature = string.Empty };
        request = request with { SenderSignature = borrowedSourceMac.SignNativeRequest(request) };
        await client.RequireBeforeDispatchAsync(destination, request, borrowedSourceMac, originalToken).ConfigureAwait(false);
        var actual = await client.DispatchAsync(actualAddress, request, originalToken).ConfigureAwait(false);
        var verified = await client.VerifyReplyAsync(destination, request, actual, maximumReplyBytes, originalToken).ConfigureAwait(false);
        return new(ordinal, fault.Variation, request, verified);
    }
}
