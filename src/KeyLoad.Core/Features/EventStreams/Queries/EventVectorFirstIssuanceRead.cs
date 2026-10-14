using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string InvalidEventVectorFirstRead = "The original event vector first issuance read is inconsistent.";

    internal EventVectorFirstIssuanceObservation ReadEventVectorFirstIssuance(string principalId,
        EventFeedControlRequest request, PhysicalShardRecord controlOwner, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(controlOwner);
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        return Store.Read(view => ReadEventVectorFirstIssuance(view, principalId, request,
            controlOwner, work, cancellationToken));
    }

    private EventVectorFirstIssuanceObservation ReadEventVectorFirstIssuance(IKeyValueView view,
        string principalId, EventFeedControlRequest request, PhysicalShardRecord controlOwner,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var bounded = work.CreateView(view);
        var principal = Principal(bounded, principalId, EvaluationClock.GetUtcNow());
        if (request.ControlPartition is null || request.Scope is null
            || request.Action != EventFeedControlAction.Open
            || request.OriginalFirstIssuancePublicCommandId != Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidEventVectorFirstRead);
        }
        Authorization.Require(principal, request.ControlPartition, request.Scope.Resource,
            Capability.SubscriptionsManage);
        _ = StreamTraversalPlacement(bounded, request.ControlPartition, controlOwner);
        cancellationToken.ThrowIfCancellationRequested();
        var result = EventVectorFirstIssuanceLookup.Read(bounded, principal, request, controlOwner,
            Store.Identity, Authorization, OperationLimitsOptions, cancellationToken);
        work.CheckResult(result);
        return result;
    }
}
