using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorOpenSourceAcknowledgement
{
    internal static async Task<EventVectorSourcePhase> ExecuteAsync(RemoteDocumentRuntime runtime,
        OrleansNode node, PartitionHost partition, NodeOptions options, TimeProvider clock,
        DecodedGrainRequest parent, EventVectorControlPhase originalOpen, EventVectorSourcePhase admitted,
        EventVectorAdmissionPolicy admission, EventVectorRemoteEncoding encoding,
        ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        var principal = GrainRequestAuthority.ReloadForRequest(partition.Database, parent.Envelope, clock);
        var directory = partition.Database.ReadEventVectorConfiguredOwners(principal.Id,
            originalOpen.OriginalRequest, work, originalCancellationToken);
        var destination = directory.Owners.SingleOrDefault(entry =>
            PhysicalOwnerEntryValidation.SameOwner(entry.Owner, admitted.SourceOwner))
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Unavailable);
        using var call = EventVectorOutgoingCall.Create(node, partition, options,
            EventVectorRemoteAction.SourceEffect, admitted, parent.Envelope.ExpiresAt, work, encoding);
        var local = PhysicalOwnerConfiguredTuples.Local(options, partition).Owner;
        using var exchange = PhysicalOwnerEntryValidation.SameOwner(local, destination.Owner)
            ? await runtime.SendLocalEventVectorAsync(call.OriginalCall, work, originalCancellationToken).ConfigureAwait(false)
            : await runtime.SendRemoteEventVectorAsync(destination, call.OriginalCall,
                encoding, work, originalCancellationToken).ConfigureAwait(false);
        work.ChargeBytes(exchange.Reply.Reply.OriginalNativeReply.Length);
        work.ChargeBytes(exchange.Reply.OriginalReplyBytes.Length);
        work.ChargeBytes(SHA256.HashSizeInBytes);
        var observed = await EventVectorSourceReplyAuthorization.ReadAsync(runtime, node, partition,
            options, clock, parent, originalOpen, admitted, call.OriginalCall, exchange.Reply,
            admission, encoding, work, originalCancellationToken).ConfigureAwait(false);
        try
        {
            var phase = EventVectorOpenControlPhases.Observe(originalOpen, observed);
            var actual = await EventVectorParentControlCall.ExecuteAsync(node, partition, clock,
                parent, phase, work, originalCancellationToken).ConfigureAwait(false);
            work.Check();
            return EventVectorOpenSourceResult.Require(actual, admitted, observed, admission);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(observed.SourceObservationWitness).Span);
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(observed.SourceOriginalOutcomeDigest).Span);
        }
    }
}
