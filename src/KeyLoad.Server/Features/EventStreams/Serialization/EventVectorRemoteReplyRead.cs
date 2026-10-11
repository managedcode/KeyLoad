using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorRemoteReplyRead
{
    private const long MinimumBodyBytes = 1;
    internal static async Task<EventVectorMacCheckedReply> ReadAsync(HttpResponseMessage response,
        EventVectorRemoteCall originalCall, RemoteDocumentMac actualReceiverMac,
        EventVectorRemoteEncoding encoding, ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(originalCall);
        ArgumentNullException.ThrowIfNull(actualReceiverMac);
        ArgumentNullException.ThrowIfNull(encoding);
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        { throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Unavailable); }
        ArgumentNullException.ThrowIfNull(work);
        var signature = Signature(response);
        if (response.Content.Headers.ContentLength is not { } originalLength
            || originalLength < MinimumBodyBytes || originalLength > encoding.MaximumBytes)
        { throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Unavailable); }
        work.ChargeBytes(checked(originalLength + Encoding.UTF8.GetByteCount(signature)));
        var originalBytes = await RemoteDocumentWire.ReadReplyAsync(response.Content,
            originalCancellationToken, encoding.MaximumBytes).ConfigureAwait(false);
        var transferred = false;
        try
        {
            if (!actualReceiverMac.Verify(originalBytes, signature, reply: true))
            { throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid); }
            var reply = encoding.DecodeAuthenticatedReply(originalBytes, originalCall);
            originalCancellationToken.ThrowIfCancellationRequested();
            var result = new EventVectorMacCheckedReply(reply, originalBytes, signature);
            transferred = true;
            return result;
        }
        finally
        {
            if (!transferred)
            { CryptographicOperations.ZeroMemory(originalBytes); }
        }
    }

    private static string Signature(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(RemoteDocumentProtocol.SignatureHeader, out var values))
        { throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid); }
        using var iterator = values.GetEnumerator();
        if (!iterator.MoveNext())
        { throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid); }
        var signature = iterator.Current;
        if (signature is null || iterator.MoveNext())
        { throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid); }
        return signature;
    }
}
