using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorEndpointReply
{
    internal static async Task WriteAsync(HttpContext context, EventVectorAdmittedCall admitted,
        EventVectorRemoteReceiver receiver, EventVectorRemoteCallValidation authority,
        RemoteDocumentMac originalReplyMac, EventVectorRemoteEncoding encoding,
        CancellationToken originalCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(admitted);
        ArgumentNullException.ThrowIfNull(receiver);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(originalReplyMac);
        ArgumentNullException.ThrowIfNull(encoding);
        var call = admitted.Original.Call;
        var work = receiver.CreateWork(admitted, originalCancellationToken);
        var result = ReadOnlyMemory<byte>.Empty;
        ErrorCode? errorCode = null;
        try
        { result = await receiver.ExecuteAsync(admitted, work, originalCancellationToken).ConfigureAwait(false); }
        catch (KeyLoadException error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { errorCode = error.Code; }
        byte[]? originalBytes = null;
        try
        {
            authority.Require(call, originalCancellationToken);
            var reply = new EventVectorRemoteReply(EventVectorRemoteProtocol.Version, call.RequestId,
                call.Nonce, EventVectorRemoteProtocol.Purpose, call.Action, receiver.Discovery(), result,
                errorCode, errorCode is null ? null : EventVectorRemoteProtocol.Unavailable);
            work.ChargeBytes(RemoteDocumentProtocol.SignatureCharacters);
            originalBytes = EventVectorWorkEncoding.Encode(reply, encoding, work);
            context.Response.Headers[RemoteDocumentProtocol.SignatureHeader] = originalReplyMac.Sign(originalBytes, reply: true);
            context.Response.ContentType = RemoteDocumentProtocol.ContentType;
            context.Response.ContentLength = originalBytes.LongLength;
            await context.Response.Body.WriteAsync(originalBytes, originalCancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (originalBytes is not null)
            { CryptographicOperations.ZeroMemory(originalBytes); }
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(result).Span);
        }
    }
}
