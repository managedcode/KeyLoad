using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorRemoteExchange
{
    private const int NoFailures = 0;

    internal static async Task<EventVectorAuthenticatedExchange> SendAsync(RemoteDocumentClient client, NodeOptions options,
        IOptions<OrleansMembershipOptions> membership, TimeProvider clock,
        RegisteredPhysicalOwnerV1 destination, EventVectorRemoteCall originalCall,
        EventVectorRemoteEncoding encoding, ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        originalCancellationToken.ThrowIfCancellationRequested();
        EventVectorRemoteReplyAuthority.RequireDestination(destination, options);
        ArgumentNullException.ThrowIfNull(work);
        var measuredRequestBytes = NativeSerialization.Measure(originalCall);
        work.ChargeBytes(checked(measuredRequestBytes + RemoteDocumentProtocol.SignatureCharacters));
        var body = encoding.Encode(originalCall);
        EventVectorMacCheckedReply? result = null;
        var failures = new List<Exception>();
        string? originalRequestSignature = null;
        var transferred = false;
        try
        {
            if (body.Length != measuredRequestBytes)
            {
                throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid);
            }
            await EventVectorRemoteRequestSend.SendAsync(client, options, membership, clock,
                destination, originalCall, body, encoding, work, failures,
                (checkedReply, signature) => { result = checkedReply; originalRequestSignature = signature; },
                originalCancellationToken).ConfigureAwait(false);
            if (failures.Count != NoFailures || result is null || originalRequestSignature is null)
            {
                ServerFailureObserver.ThrowIfAny(failures);
                throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid);
            }
            var owned = new EventVectorAuthenticatedExchange(body, originalRequestSignature, result);
            transferred = true;
            return owned;
        }
        finally
        {
            if (!transferred)
            {
                CryptographicOperations.ZeroMemory(body);
                if (result is { } retained)
                {
                    CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsMemory(retained.OriginalReplyBytes).Span);
                    CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsMemory(retained.Reply.OriginalNativeReply).Span);
                }
            }
        }
    }
}
