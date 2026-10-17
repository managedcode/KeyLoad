using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorFreshSourceAdmission
{
    private const long FirstAppendPosition = 1;
    private const string Invalid = "The original event vector source admission requires recovery.";

    internal static async Task<EventVectorSourcePhase> ExecuteAsync(RemoteDocumentRuntime runtime,
        OrleansNode node, PartitionHost partition, NodeOptions options, TimeProvider clock,
        DecodedGrainRequest parent, EventVectorControlPhase phase, EventVectorAdmissionPolicy admission,
        EventVectorRemoteEncoding encoding, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var reply = await EventVectorParentControlCall.ExecuteReplyAsync(node, partition, clock,
            parent, phase, work, cancellationToken).ConfigureAwait(false);
        EventVectorCurrentSourceRound? fresh = null;
        EventVectorSourcePhase? result = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var principal = GrainRequestAuthority.ReloadForRequest(partition.Database, parent.Envelope, clock);
                var directory = partition.Database.ReadEventVectorConfiguredOwners(principal.Id,
                    phase.OriginalRequest, work, cancellationToken);
                fresh = await EventVectorCurrentSourceRound.CreateAsync(runtime, node, partition, options,
                    directory, phase.OriginalRequest, principal.Id, parent.Envelope.ExpiresAt,
                    admission, encoding, work, cancellationToken).ConfigureAwait(false);
                work.Check();
                result = Require(GrainNativePayload.Read<GrainValue>(reply.Payload).Value, parent, phase, admission);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            if (fresh is not null)
            {
                ServerFailureObserver.Observe(fresh.Dispose, failures);
            }
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(reply.Payload).Span);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
    }

    private static EventVectorSourcePhase Require(object? value, DecodedGrainRequest parent,
        EventVectorControlPhase phase, EventVectorAdmissionPolicy admission)
    {
        var principal = parent.Envelope.PrincipalId ?? throw Errors.Fail(ErrorCode.Unauthenticated, Invalid);
        var identity = EventVectorControlOperationIdentity.Create(phase, principal, admission);
        if (phase.Action != EventVectorControlAction.AdmitSource || phase.IntendedSourcePhase is not { } intended
            || phase.OriginalParentRequestId != parent.Envelope.RequestId || phase.OriginalParentExpiresAt != parent.Envelope.ExpiresAt
            || value is not EventVectorSourceAdmitAppendResult appended || appended.OriginalOutcome.Error is not null
            || appended.OperationId != identity.CommandId || appended.OriginalParentRequestId != parent.Envelope.RequestId
            || appended.OriginalSourcePhaseCommandId != intended.PhaseCommandId
            || appended.AppendedIndex < FirstAppendPosition || appended.AppendedTerm < FirstAppendPosition
            || appended.OriginalOutcome.NativeValue is not EventVectorControlResult control
            || control.CommandId != appended.OperationId || control.Receipt.CommandId != appended.OperationId
            || control.Receipt.Token.Position != appended.AppendedIndex || control.SourcePhase is not { } actual
            || actual.PhaseCommandId != intended.PhaseCommandId
            || !actual.OriginalNativeBody.Span.SequenceEqual(intended.OriginalNativeBody.Span)
            || !actual.OriginalBodyDigest.Span.SequenceEqual(intended.OriginalBodyDigest.Span))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        _ = EventVectorSourceMutationValidation.ReadOriginal(actual, admission);
        return actual;
    }
}
