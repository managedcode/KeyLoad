using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorRefreshCompletion
{
    private const int Version = 1;
    private const int FirstOrdinal = 0;

    internal static async Task ExecuteAsync(RemoteDocumentRuntime runtime, OrleansNode node,
        PartitionHost partition, NodeOptions options, TimeProvider clock, DecodedGrainRequest parent,
        EventVectorPreparedRefresh prepared, EventVectorAdmissionPolicy admission,
        EventVectorRemoteEncoding encoding, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var terminal = new EventVectorControlPhase(Version, prepared.Request, EventVectorControlAction.PublishRefresh,
            prepared.Map.Revision, prepared.Map.CoverageGeneration, prepared.Map.CleanupGeneration,
            null, null, null, prepared.EncodedCandidates, prepared.CandidateChecksum)
        {
            OriginalCoverageWitness = prepared.Original.OriginalCoverageWitness,
            OriginalParentExpiresAt = parent.Envelope.ExpiresAt,
            OriginalParentRequestId = parent.Envelope.RequestId
        };
        EventVectorControlFields.Require(terminal, admission);
        for (var ordinal = FirstOrdinal; ordinal < prepared.Candidates.Length; ordinal++)
        {
            work.Check();
            using var intended = new EventVectorOpenPinOwner(EventVectorRefreshSourcePreparation.Candidate(
                prepared, ordinal, parent.Envelope.ExpiresAt, admission, work));
            await SettleAsync(runtime, node, partition, options, clock, parent, terminal,
                intended.Phase, admission, encoding, work, cancellationToken).ConfigureAwait(false);
        }
        for (var ordinal = FirstOrdinal; ordinal < prepared.Original.Entries.Length; ordinal++)
        {
            if (prepared.Candidates.Any(entry => entry.Source == prepared.Original.Entries[ordinal].Source))
            {
                continue;
            }
            work.Check();
            using var intended = new EventVectorOpenPinOwner(EventVectorRefreshSourcePreparation.Removed(
                prepared, ordinal, parent.Envelope.ExpiresAt, admission, work));
            await SettleAsync(runtime, node, partition, options, clock, parent, terminal,
                intended.Phase, admission, encoding, work, cancellationToken).ConfigureAwait(false);
        }
        await EventVectorParentControlCall.SubmitWithoutDecodingAsync(node, partition, clock, parent,
            terminal, work, cancellationToken).ConfigureAwait(false);
        work.Check();
    }

    private static async Task SettleAsync(RemoteDocumentRuntime runtime, OrleansNode node,
        PartitionHost partition, NodeOptions options, TimeProvider clock, DecodedGrainRequest parent,
        EventVectorControlPhase terminal, EventVectorSourcePhase intended, EventVectorAdmissionPolicy admission,
        EventVectorRemoteEncoding encoding, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var control = EventVectorOpenControlPhases.Admit(terminal, intended);
        using var admitted = new EventVectorOpenPinOwner(await EventVectorFreshSourceAdmission.ExecuteAsync(
            runtime, node, partition, options, clock, parent, control, admission,
            encoding, work, cancellationToken).ConfigureAwait(false));
        using var observed = new EventVectorOpenPinOwner(await EventVectorOpenSourceAcknowledgement.ExecuteAsync(
            runtime, node, partition, options, clock, parent, terminal, admitted.Phase,
            admission, encoding, work, cancellationToken).ConfigureAwait(false));
    }
}
