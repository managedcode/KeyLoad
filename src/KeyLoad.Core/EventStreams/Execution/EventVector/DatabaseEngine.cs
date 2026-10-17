using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int EventVectorFirstCleanupVersion = 1;
    private const long EventVectorFirstCleanupRevision = 0;
    private const string InvalidEventVectorFirstCleanup = "The original first issuance cleanup requires recovery.";

    internal EventVectorPreparedFirstCleanup PrepareVerifiedEventVectorFirstCleanup(string principalId,
        EventFeedControlRequest request, PhysicalShardRecord controlOwner, DateTimeOffset originalParentExpiry,
        ReadOnlyMemory<byte> freshSourceWitness, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(controlOwner);
        ArgumentNullException.ThrowIfNull(work);
        cancellationToken.ThrowIfCancellationRequested();
        return Store.Read(view => PrepareEventVectorFirstCleanupOwned(work.CreateView(view), principalId,
            request, controlOwner, originalParentExpiry, freshSourceWitness, work, cancellationToken));
    }

    private EventVectorPreparedFirstCleanup PrepareEventVectorFirstCleanupOwned(IKeyValueView bounded,
        string principalId, EventFeedControlRequest request, PhysicalShardRecord controlOwner,
        DateTimeOffset originalParentExpiry, ReadOnlyMemory<byte> freshSourceWitness,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var principal = Principal(bounded, principalId, EvaluationClock.GetUtcNow());
        if (request.ControlPartition is null || request.Scope is null || request.Action != EventFeedControlAction.Release
            || request.OriginalFirstIssuancePublicCommandId == Guid.Empty
            || request.CommandId == request.OriginalFirstIssuancePublicCommandId)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidEventVectorFirstCleanup);
        }
        Authorization.Require(principal, request.ControlPartition, request.Scope.Resource, Capability.SubscriptionsManage);
        _ = StreamTraversalPlacement(bounded, request.ControlPartition, controlOwner);
        var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
        using var owned = new EventVectorFirstIssuanceOwnedRow(EventVectorFirstIssuanceValidation.Read(
            bounded, request.ControlPartition, principal.Id, request.OriginalFirstIssuancePublicCommandId,
            terminal: false, admission));
        var row = owned.Row;
        EventVectorParentScopes.Authorize(Authorization, principal, row.OriginalAuthorizedSourceScopes, admission);
        _ = EventVectorCurrentSourceAuthorization.Require(bounded, principal, request,
            row.OriginalAuthorizedSourceScopes, freshSourceWitness, admission);
        if (EventVectorRowStorage.Read<EventVectorMap>(bounded,
            EventVectorKeys.Map(request.ControlPartition, request.MapId), admission) is not null)
        {
            throw Errors.Fail(ErrorCode.Conflict, InvalidEventVectorFirstCleanup);
        }
        var action = row.State == EventVectorFirstIssuanceState.Abandoned
            ? EventVectorControlAction.CleanupAbandonedIssuance : EventVectorControlAction.AbandonFirstIssuance;
        var phase = new EventVectorControlPhase(EventVectorFirstCleanupVersion, request, action,
            EventVectorFirstCleanupRevision, EventVectorFirstCleanupRevision, EventVectorFirstCleanupRevision,
            null, null, null, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty)
        {
            OriginalCoverageWitness = row.OriginalCoverageWitnessBytes,
            OriginalCapturedFirstOpenPhaseBytes = row.OriginalCapturedFirstOpenPhaseBytes,
            OriginalFirstIssuancePublicCommandId = row.OriginalPublicCommandId,
            OriginalParentExpiresAt = originalParentExpiry
        };
        EventVectorControlFields.Require(phase, admission);
        _ = EventVectorFirstReleaseAdmission.Require(bounded, principal, phase,
            originalOutcome: false, Authorization, admission);
        work.ChargeBytes(checked((long)phase.OriginalCoverageWitness.Length + phase.OriginalCapturedFirstOpenPhaseBytes.Length));
        cancellationToken.ThrowIfCancellationRequested();
        return new(phase);
    }
}
