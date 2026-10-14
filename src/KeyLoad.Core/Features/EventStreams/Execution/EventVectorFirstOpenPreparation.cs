using KeyLoad.Core.Features.ClusterRouting.Serialization;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int FirstEventVectorSourceOrdinal = 0;
    private const string InvalidFirstEventVectorPreparation = "The original admitted event vector Open requires recovery.";

    internal EventVectorPreparedFirstOpen PrepareVerifiedEventVectorFirstOpen(string principalId,
        EventVectorControlPhase originalAdmit, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalAdmit);
        ArgumentNullException.ThrowIfNull(work);
        cancellationToken.ThrowIfCancellationRequested();
        work.Check();
        return Store.Read(view =>
        {
            var bounded = work.CreateView(view);
            var principal = Principal(bounded, principalId, EvaluationClock.GetUtcNow());
            var actual = RequireEventVectorControlView(bounded, principal, originalAdmit);
            var request = originalAdmit.OriginalRequest;
            var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
            var issuance = EventVectorFirstIssuanceValidation.Read(bounded, request.ControlPartition,
                principal.Id, request.CommandId, terminal: false, admission);
            EventVectorFirstIssuanceValidation.RequireRequest(issuance, request, admission);
            if (originalAdmit.Action != EventVectorControlAction.AdmitFirstIntent
                || issuance.State != EventVectorFirstIssuanceState.Admitted
                || issuance.OriginalParentRequestId != originalAdmit.OriginalParentRequestId
                || issuance.OriginalParentExpiresAt != originalAdmit.OriginalParentExpiresAt
                || issuance.OriginalParentExpiresAt <= EvaluationClock.GetUtcNow()
                || !issuance.OriginalCoverageWitnessBytes.Span.SequenceEqual(originalAdmit.OriginalCoverageWitness.Span))
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidFirstEventVectorPreparation);
            }
            var catalog = PhysicalShardCatalogRecordSerialization.Read(bounded)
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, InvalidFirstEventVectorPreparation);
            var owner = ResolveRegisteredPlacementOwner(bounded, catalog.DefaultShard,
                actual.PhysicalShardId, ErrorCode.OwnershipLost);
            var witness = EventVectorCoverageWitnessEncoding.Read(issuance.OriginalCoverageWitnessBytes, request, admission);
            using var decoded = new EventVectorFirstOpenDecodedOwner(witness);
            var projection = decoded.Projection;
            var entries = EventVectorEntryEncoding.Decode(projection.OriginalEncodedEntries,
                EventVectorEntryEncoding.Digest(projection.OriginalEncodedEntries), admission);
            EventVectorSourcePhase? first = null;
            if (!entries.IsEmpty)
            {
                var entry = entries[FirstEventVectorSourceOrdinal];
                var sourceOwner = EventVectorOriginalEntryOwner.Require(witness, entry, admission, work);
                first = EventVectorInitialPinPreparation.Prepare(issuance, principal, owner, entry, sourceOwner, admission, work);
            }
            return EventVectorFirstOpenOwnedEncoding.Create(originalAdmit, projection.OriginalEncodedEntries,
                first, admission, work, cancellationToken);
        });
    }
}
