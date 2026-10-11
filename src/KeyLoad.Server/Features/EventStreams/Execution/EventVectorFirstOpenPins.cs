using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorFirstOpenPins
{
    private const int FirstOrdinal = 0;
    private const int IncludedOrdinal = 1;
    private const string Invalid = "The original complete event vector Open requires recovery.";

    internal static async Task<EventVectorControlResult> CompleteAsync(RemoteDocumentRuntime runtime,
        OrleansNode node, PartitionHost partition, NodeOptions options, TimeProvider clock,
        DecodedGrainRequest parent, EventVectorControlPhase originalOpen, EventVectorControlResult opened,
        EventVectorAdmissionPolicy admission, EventVectorRemoteEncoding encoding,
        ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        var map = RequireOpening(originalOpen, opened, partition.Database.Store.Identity.Incarnation, admission);
        var entries = EventVectorEntryEncoding.Decode(originalOpen.OriginalEncodedEntries,
            originalOpen.OriginalEntryChecksum, admission);
        if (originalOpen.IntendedSourcePhase is { } first)
        {
            using var acknowledgement = new EventVectorOpenPinOwner(
                await EventVectorOpenSourceAcknowledgement.ExecuteAsync(runtime, node, partition,
                    options, clock, parent, originalOpen, first, admission, encoding,
                    work, originalCancellationToken).ConfigureAwait(false));
        }
        for (var ordinal = FirstOrdinal + IncludedOrdinal; ordinal < entries.Length; ordinal++)
        {
            await AdmitAndAcknowledgeAsync(runtime, node, partition, options, clock, parent,
                originalOpen, ordinal, admission, encoding, work, originalCancellationToken).ConfigureAwait(false);
        }
        var publish = EventVectorOpenControlPhases.Publish(originalOpen, map);
        var result = await EventVectorParentControlCall.ExecuteAsync(node, partition, clock,
            parent, publish, work, originalCancellationToken).ConfigureAwait(false);
        work.Check();
        if (result is not EventVectorControlResult actual || actual.Map is not { } published
            || published.State != EventVectorMapState.Active || published.PendingPhaseId is not null
            || published.MapId != map.MapId || published.OriginalOpenCommandId != parent.Envelope.CommandId
            || actual.OriginalPublicResultBytes.IsEmpty)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        return actual;
    }

    private static EventVectorMap RequireOpening(EventVectorControlPhase original, EventVectorControlResult result,
        Guid incarnation, EventVectorAdmissionPolicy admission)
    {
        EventVectorControlFields.Require(original, admission);
        if (result.Map is not { } map || map.State != EventVectorMapState.Opening
            || map.MapId != original.OriginalRequest.MapId || map.ControlPartition != original.OriginalRequest.ControlPartition
            || map.ControlIncarnation != incarnation || map.OriginalOpenCommandId != original.OriginalRequest.CommandId
            || map.PendingPhaseId != original.IntendedSourcePhase?.PhaseCommandId
            || result.Receipt.CommandId != result.CommandId)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        return map;
    }

    private static async Task AdmitAndAcknowledgeAsync(RemoteDocumentRuntime runtime, OrleansNode node,
        PartitionHost partition, NodeOptions options, TimeProvider clock, DecodedGrainRequest parent,
        EventVectorControlPhase original, int ordinal, EventVectorAdmissionPolicy admission,
        EventVectorRemoteEncoding encoding, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        using var intended = new EventVectorOpenPinOwner(partition.Database.PrepareVerifiedNextEventVectorOpenPin(
            parent.Envelope.PrincipalId ?? throw Errors.Fail(ErrorCode.Unauthenticated, Invalid),
            original, ordinal, work, cancellationToken));
        var phase = EventVectorOpenControlPhases.Admit(original, intended.Phase);
        var actual = await EventVectorFreshSourceAdmission.ExecuteAsync(runtime, node, partition,
            options, clock, parent, phase, admission, encoding, work, cancellationToken).ConfigureAwait(false);
        using var admitted = new EventVectorOpenPinOwner(actual);
        using var acknowledgement = new EventVectorOpenPinOwner(await EventVectorOpenSourceAcknowledgement.ExecuteAsync(
            runtime, node, partition, options, clock, parent, original, actual, admission,
            encoding, work, cancellationToken).ConfigureAwait(false));
    }
}
