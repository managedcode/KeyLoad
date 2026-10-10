using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int EventVectorParentObservationVersion = 1;
    private const string InvalidEventVectorParentObservation = "The original event vector parent observation authority is inconsistent.";

    internal EventVectorParentObservation ReadEventVectorParentOutcome(string principalId,
        EventFeedControlRequest request, PhysicalShardRecord controlOwner, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(controlOwner);
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        return Store.Read(view => ReadEventVectorParentOutcome(view, principalId, request, controlOwner, work, cancellationToken));
    }

    private EventVectorParentObservation ReadEventVectorParentOutcome(IKeyValueView view, string principalId,
        EventFeedControlRequest request, PhysicalShardRecord controlOwner, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var bounded = work.CreateView(view);
        var principal = Principal(bounded, principalId, EvaluationClock.GetUtcNow());
        if (request.ControlPartition is null || request.Scope is null)
        { throw Errors.Fail(ErrorCode.Validation, InvalidEventVectorParentObservation); }
        Authorization.Require(principal, request.ControlPartition, request.Scope.Resource,
            Capability.SubscriptionsManage);
        _ = StreamTraversalPlacement(bounded, request.ControlPartition, controlOwner);
        var original = EventVectorParentObservationRead.Read(bounded, Authorization, principal, request,
            Store.Identity.Incarnation, OperationLimitsOptions, cancellationToken);
        var identity = Store.Identity;
        var result = new EventVectorParentObservation(EventVectorParentObservationVersion, principal.Id,
            request.CommandId, controlOwner, identity.NodeId, identity.ReadGeneration, original.Phase, original.Outcome);
        work.CheckResult(result);
        return result;
    }
}
