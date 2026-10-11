using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorSettlementCompletion
{
    private const int Version = 1;
    private const int FirstOrdinal = 0;

    internal static async Task ExecuteAsync(RemoteDocumentRuntime runtime, OrleansNode node,
        PartitionHost partition, NodeOptions options, TimeProvider clock, DecodedGrainRequest parent,
        EventFeedControlRequest request, EventVectorPreparedSettlement prepared,
        EventVectorAdmissionPolicy admission, EventVectorRemoteEncoding encoding,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var terminal = Terminal(parent, request, prepared, admission);
        for (var ordinal = FirstOrdinal; ordinal < prepared.Owned.Entries.Length; ordinal++)
        {
            work.Check();
            await AdvanceSourceAsync(runtime, node, partition, options, clock, parent, terminal,
                prepared, ordinal, admission, encoding, work, cancellationToken).ConfigureAwait(false);
        }
        await EventVectorParentControlCall.SubmitWithoutDecodingAsync(node, partition, clock, parent,
            terminal, work, cancellationToken).ConfigureAwait(false);
        work.Check();
    }

    private static EventVectorControlPhase Terminal(DecodedGrainRequest parent, EventFeedControlRequest request,
        EventVectorPreparedSettlement prepared, EventVectorAdmissionPolicy admission)
    {
        if (request.Action is not (EventFeedControlAction.Acknowledge or EventFeedControlAction.Release)
            || prepared.Owned.Entries.Length != prepared.TargetEntries.Length
            || prepared.Owned.Entries.Length != prepared.Owned.OriginalPinPhaseBytes.Length
            || prepared.Owned.Entries.Length != prepared.CurrentSourcePolicyEpochs.Length)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, EventVectorRemoteProtocol.Invalid); }
        var action = request.Action == EventFeedControlAction.Acknowledge
            ? EventVectorControlAction.PublishAcknowledgement : EventVectorControlAction.CompleteRelease;
        var phase = new EventVectorControlPhase(Version, request, action, prepared.Owned.Revision,
            prepared.Owned.CoverageGeneration, prepared.Owned.CleanupGeneration, null, null, null,
            prepared.TargetBytes, prepared.TargetDigest)
        {
            OriginalCoverageWitness = prepared.Owned.OriginalCoverageWitness,
            OriginalParentExpiresAt = parent.Envelope.ExpiresAt,
            OriginalParentRequestId = parent.Envelope.RequestId
        };
        EventVectorControlFields.Require(phase, admission);
        return phase;
    }

    private static async Task AdvanceSourceAsync(RemoteDocumentRuntime runtime, OrleansNode node,
        PartitionHost partition, NodeOptions options, TimeProvider clock, DecodedGrainRequest parent,
        EventVectorControlPhase terminal, EventVectorPreparedSettlement prepared, int ordinal,
        EventVectorAdmissionPolicy admission, EventVectorRemoteEncoding encoding,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var bytes = prepared.Owned.OriginalPinPhaseBytes[ordinal];
        admission.RequireEncodedBytes(bytes.Length);
        using var settled = new EventVectorOpenPinOwner(NativeSerialization.Deserialize<EventVectorSourcePhase>(bytes.Span));
        var role = terminal.OriginalRequest.Action == EventFeedControlAction.Acknowledge
            ? EventVectorSourcePhaseRole.Advance : EventVectorSourcePhaseRole.Release;
        var mutation = EventVectorSettledSourceMutation.Create(prepared.Map,
            prepared.Owned.Entries[ordinal], prepared.TargetEntries[ordinal], settled.Phase,
            role, parent.Envelope.ExpiresAt, prepared.CurrentSourcePolicyEpochs[ordinal], admission);
        var state = role == EventVectorSourcePhaseRole.Advance
            ? EventVectorMapState.Acknowledging : EventVectorMapState.Releasing;
        using var intended = new EventVectorOpenPinOwner(EventVectorPhasePreparation.Prepare(
            prepared.Map with { State = state }, ordinal, prepared.Owned.Entries[ordinal], mutation, admission));
        var control = EventVectorOpenControlPhases.Admit(terminal, intended.Phase);
        using var admitted = new EventVectorOpenPinOwner(await EventVectorFreshSourceAdmission.ExecuteAsync(
            runtime, node, partition, options, clock, parent, control, admission,
            encoding, work, cancellationToken).ConfigureAwait(false));
        using var observed = new EventVectorOpenPinOwner(await EventVectorOpenSourceAcknowledgement.ExecuteAsync(
            runtime, node, partition, options, clock, parent, terminal, admitted.Phase,
            admission, encoding, work, cancellationToken).ConfigureAwait(false));
    }
}
