using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const long BeforeFirstAdmitPosition = 0;

    /// <summary>Classifies only a Core-issued first Admit and authorizes its canonical journal inspection.</summary>
    /// <param name="operation">The original native operation owned by the existing append owner.</param>
    /// <param name="cancellationToken">Original submission cancellation, without renewed phase expiry.</param>
    /// <returns>A borrowed purpose-owned grant, or null for the unchanged non-first-Admit path.</returns>
    public EventVectorFirstAdmitProbe? OpenEventVectorFirstAdmitProbe(ReplicatedOperation operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Kind != OperationKind.EventFeedControl)
        { return null; }
        var verified = VerifyOperationAuthority(operation);
        RequireNativeBudget(System.Text.Encoding.UTF8.GetByteCount(verified.PayloadJson));
        if (EventVectorControlMetadataAction.Read(verified.PayloadJson) != EventVectorControlAction.AdmitFirstIntent)
        { return null; }
        var metadata = System.Text.Json.JsonSerializer.Deserialize<EventVectorFirstIssuanceIdentity>(
            verified.PayloadJson, JsonDefaults.Options);
        if (metadata is null || metadata.Action != EventVectorControlAction.AdmitFirstIntent)
        { return null; }
        var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
        if (metadata.PrincipalId != verified.PrincipalId
            || EventVectorFirstIssuanceIds.For(metadata, admission) != verified.Id)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidEventVectorControlAuthority); }
        var work = new ReadExecutionBudget(OperationLimitsOptions, EvaluationClock, cancellationToken);
        var grant = work.CreateReadGrant(work.RemainingReadGrantBytes, work.RemainingReadGrantRecords);
        var probe = new EventVectorFirstAdmitProbe(work, grant);
        try
        {
            using var scope = work.EnterReadGrant(grant);
            Store.Read(view => ReadAuthorizedEventVectorFirstAdmit(view, verified, work, probe));
            return probe;
        }
        catch (Exception primary)
        {
            probe.DisposeAfterFailure(primary);
            throw;
        }
    }
    private bool ReadAuthorizedEventVectorFirstAdmit(IKeyValueView view,
        ReplicatedOperation verified, ReadExecutionBudget work, EventVectorFirstAdmitProbe probe)
    {
        var bounded = work.CreateView(view);
        var principal = Principal(bounded, verified.PrincipalId, EvaluationClock.GetUtcNow());
        var phase = Payload<EventVectorControlPhase>(verified);
        if (phase.Action != EventVectorControlAction.AdmitFirstIntent)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidEventVectorControlAuthority); }
        work.ConstrainLifetime(phase.OriginalParentExpiresAt);
        RequireCurrentEventVectorFirstAdmitView(bounded, principal, phase);
        var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
        var identity = EventVectorControlOperationIdentity.Create(phase, principal.Id, admission);
        if (verified.Id != identity.CommandId || verified.PayloadJson != identity.Metadata)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidEventVectorControlAuthority); }
        var original = EventVectorFirstAdmitOutcome.Observe(bounded, principal,
            phase.OriginalRequest, Store.Identity.Incarnation, Authorization, admission);
        if (original.Outcome is null)
        { _ = RequireEventVectorControlView(bounded, principal, phase); }
        probe.OriginalParentRequestId = phase.OriginalParentRequestId;
        probe.OriginalOutcome = original.Outcome?.Result;
        probe.AppliedPosition = bounded.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes
            ? NativeSerialization.Deserialize<long>(bytes) : BeforeFirstAdmitPosition;
        return true;
    }

    private void RequireCurrentEventVectorFirstAdmitView(IKeyValueView view, PrincipalRecord principal,
        EventVectorControlPhase phase)
    {
        var request = phase.OriginalRequest;
        var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
        Authorization.Require(principal, request.ControlPartition, request.Scope.Resource, Capability.SubscriptionsManage);
        EventVectorControlFields.Require(phase, admission);
        RequireEventVectorControlSources(principal, phase, admission);
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, InvalidEventVectorControlAuthority);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        var placement = ResolveRegisteredPlacement(view, request.ControlPartition, catalog.DefaultShard);
        var owner = ResolveRegisteredPlacementOwner(view, catalog.DefaultShard,
            placement.PhysicalShardId, ErrorCode.OwnershipLost);
        _ = StreamTraversalPlacement(view, request.ControlPartition, owner);
    }
}
