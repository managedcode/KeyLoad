using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorRemoteReplyAdmission
{
    private const int SelectedVoter = 0;

    internal static async Task ReadAsync(HttpResponseMessage response,
        EventVectorRemoteCall originalCall, RemoteDocumentMac receiverMac,
        EventVectorRemoteEncoding encoding, ReadExecutionBudget work,
        RegisteredPhysicalOwnerV1 destination, NodeOptions options,
        IOptions<OrleansMembershipOptions> membership, TimeProvider clock,
        ReplicaMembershipAuthorityAddressPins pins, string originalRequestSignature,
        Action<EventVectorMacCheckedReply, string> completed, CancellationToken originalCancellationToken)
    {
        var checkedReply = await EventVectorRemoteReplyRead.ReadAsync(response, originalCall,
            receiverMac, encoding, work, originalCancellationToken).ConfigureAwait(false);
        var transferred = false;
        try
        {
            var address = EventVectorRemoteReplyAuthority.Require(checkedReply.Reply, originalCall,
                destination, options, membership, clock, originalCancellationToken);
            await pins.PinCallerAsync(SelectedVoter, address.Endpoint.Address,
                originalCancellationToken).ConfigureAwait(false);
            if (checkedReply.Reply.Error is { } failure)
            { throw Errors.Fail(failure, checkedReply.Reply.SafeDetail ?? EventVectorRemoteProtocol.Unavailable); }
            completed(checkedReply, originalRequestSignature);
            transferred = true;
        }
        finally
        {
            if (!transferred)
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(checkedReply.OriginalReplyBytes).Span);
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(checkedReply.Reply.OriginalNativeReply).Span);
            }
        }
    }
}
