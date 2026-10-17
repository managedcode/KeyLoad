using System.Collections.Immutable;
using KeyLoad.Core.Features.AnalyticsStore;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int MinimumAnalyticsReadLimit = 0;
    private const int EqualAnalyticsIdentifierOrder = 0;
    /// <summary>Reads a bounded ID-ordered event page under fresh persisted row, field and link authorization.</summary>
    /// <param name="principalId">Persisted caller identity.</param>
    /// <param name="request">Original store, UTC range and explicit page limit.</param>
    /// <param name="cancellationToken">Original request cancellation.</param>
    /// <returns>Owned authorized rows with an explicit current-read continuation.</returns>
    public AnalyticsEventsPage ReadAnalyticsEvents(string principalId, ReadAnalyticsEventsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var work = new ReadExecutionBudget(OperationLimitsOptions, EvaluationClock, cancellationToken);
        return ReadAnalyticsEventsRead(principalId, request, work);
    }

    internal AnalyticsEventsPage ReadAnalyticsEventsBound(string principalId, ReadAnalyticsEventsRequest request,
        ReadExecutionBudget work, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.AttachResourceLifetimeBinding(binding);
        work.RequireResourceLifetimeBinding(binding);
        return ReadAnalyticsEventsRead(principalId, request, work);
    }

    private AnalyticsEventsPage ReadAnalyticsEventsRead(string principalId, ReadAnalyticsEventsRequest request,
        ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(request);
        work.Check();
        AnalyticsStoreValidation.Range(request.FromInclusive, request.ToExclusive);
        if (request.Limit <= MinimumAnalyticsReadLimit || request.Limit > Limits.MaxResults)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnalyticsStoreValidation.Budget);
        }

        if (request.AfterId is not null)
        {
            JsonData.Identifier(request.AfterId);
        }

        return Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            var scope = AnalyticsStoreReadScope.Open(this, view, principalId, request.Partition, request.Store);
            var rows = AnalyticsStoreScan.Read(this, raw, scope, request.Partition, request.Store,
                request.FromInclusive, request.ToExclusive, work);
            var candidates = rows.Where(row => request.AfterId is null || StringComparer.Ordinal.Compare(row.Id, request.AfterId) > EqualAnalyticsIdentifierOrder).ToArray();
            var more = candidates.Length > request.Limit;
            var page = candidates.Take(request.Limit).Select(row => AnalyticsStoreRowProjection.Project(this, view, scope, request.Partition, row)).ToImmutableArray();
            var result = new AnalyticsEventsPage(page, more, more ? page.Last().Id : null);
            work.CheckResult(result);
            work.Check();
            return result;
        });
    }

    /// <summary>Captures complete bounded aggregate inputs at one authorized canonical store cut.</summary>
    /// <param name="principalId">Persisted caller identity.</param>
    /// <param name="request">Exact original aggregate request.</param>
    /// <param name="cancellationToken">Original request cancellation.</param>
    /// <returns>Immutable owned inputs; no storage view escapes.</returns>
    public AnalyticsAggregateSnapshot CaptureAnalyticsAggregate(string principalId, AggregateAnalyticsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var work = new ReadExecutionBudget(OperationLimitsOptions, EvaluationClock, cancellationToken);
        return CaptureAnalyticsAggregateRead(principalId, request, work);
    }

    internal AnalyticsAggregateSnapshot CaptureAnalyticsAggregateBound(string principalId, AggregateAnalyticsRequest request,
        ReadExecutionBudget work, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.AttachResourceLifetimeBinding(binding);
        work.RequireResourceLifetimeBinding(binding);
        return CaptureAnalyticsAggregateRead(principalId, request, work);
    }

    private AnalyticsAggregateSnapshot CaptureAnalyticsAggregateRead(string principalId, AggregateAnalyticsRequest request,
        ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(request);
        work.Check();
        return Store.Read(raw =>
        {
            var scope = AnalyticsStoreReadScope.Open(this, work.CreateView(raw), principalId, request.Partition, request.Store);
            AnalyticsAggregateValidation.Require(this, scope, request);
            var rows = AnalyticsStoreScan.Read(this, raw, scope, request.Partition, request.Store,
                request.FromInclusive, request.ToExclusive, work);
            var generation = AnalyticsStoreReadScope.Generation(work.CreateView(raw), request.Partition, request.Store);
            var identity = Store.Identity;
            work.Check();
            return new AnalyticsAggregateSnapshot(request, scope.Principal, scope.Resource, scope.Lifetime, rows,
                identity.Incarnation, identity.ReadGeneration, generation, Store.Position, work, OperationLimitsOptions, EvaluationClock);
        });
    }
}
