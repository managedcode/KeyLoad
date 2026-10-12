using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorRemoteReplyAdmission
{
    internal static async Task ReadAsync(RemoteModelAuthenticatedReply nativeReply,
        EventVectorRemoteCall originalCall, RemoteDocumentMac receiverMac,
        EventVectorRemoteEncoding encoding, ReadExecutionBudget work,
        RegisteredPhysicalOwnerV1 destination, NodeOptions options,
        IOptions<OrleansMembershipOptions> membership, TimeProvider clock,
        RemoteDocumentClient client, string originalRequestSignature,
        Action<EventVectorMacCheckedReply, string> completed, CancellationToken originalCancellationToken)
    {
        EventVectorMacCheckedReply? checkedReply = null;
        var accepted = false;
        var transferred = false;
        try
        {
            checkedReply = EventVectorRemoteReplyRead.Read(nativeReply.Payload, nativeReply.Signature, originalCall,
                receiverMac, encoding, work, originalCancellationToken);
            var address = EventVectorRemoteReplyAuthority.Require(checkedReply.Reply, originalCall,
                destination, options, membership, clock, originalCancellationToken);
            await client.RequireCurrentReplyAsync(destination, nativeReply, address.ToParsableString(),
                originalCancellationToken).ConfigureAwait(false);
            accepted = true;
            if (checkedReply.Reply.Error is { } failure)
            { throw Errors.Fail(failure, checkedReply.Reply.SafeDetail ?? EventVectorRemoteProtocol.Unavailable); }
            completed(checkedReply, originalRequestSignature);
            transferred = true;
        }
        catch (Exception error) when (!accepted && nativeReply.MayHaveEffects
            && global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, EventVectorRemoteProtocol.Unavailable, error); }
        finally
        {
            if (!transferred && checkedReply is not null)
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(checkedReply.OriginalReplyBytes).Span);
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(checkedReply.Reply.OriginalNativeReply).Span);
            }
        }
    }
}
