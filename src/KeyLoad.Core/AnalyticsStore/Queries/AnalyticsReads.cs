using System.Collections.Immutable;
using KeyLoad.Core.AnalyticsStore;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Core;
/// <summary>Executes AnalyticsReads operations against the caller's database context.</summary>
public static class AnalyticsReads
{
    internal const int MinimumAnalyticsReadLimit = 0;
    internal const int EqualAnalyticsIdentifierOrder = 0;
    /// <summary>Reads a bounded ID-ordered event page under fresh persisted row, field and link authorization.</summary>
    /// <param name = "principalId">Persisted caller identity.</param>
    /// <param name = "request">Original store, UTC range and explicit page limit.</param>
    /// <param name = "cancellationToken">Original request cancellation.</param>
    /// <returns>Owned authorized rows with an explicit current-read continuation.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static AnalyticsEventsPage ReadAnalyticsEvents(this DatabaseEngine engine, string principalId, ReadAnalyticsEventsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(request);
        var work = new ReadExecutionBudget(engine.EvaluationClock, cancellationToken);
        return engine.ReadAnalyticsEventsRead(principalId, request, work);
    }

    internal static AnalyticsEventsPage ReadAnalyticsEventsBound(this DatabaseEngine engine, string principalId, ReadAnalyticsEventsRequest request, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.AttachResourceLifetimeBinding(binding);
        work.RequireResourceLifetimeBinding(binding);
        return engine.ReadAnalyticsEventsRead(principalId, request, work);
    }

    internal static AnalyticsEventsPage ReadAnalyticsEventsRead(this DatabaseEngine engine, string principalId, ReadAnalyticsEventsRequest request, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(request);
        work.Check();
        AnalyticsStoreValidation.Range(request.FromInclusive, request.ToExclusive);
        if (request.Limit <= MinimumAnalyticsReadLimit)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnalyticsStoreValidation.Budget);
        }

        if (request.AfterId is not null)
        {
            JsonData.Identifier(request.AfterId);
        }

        return engine.Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            var scope = AnalyticsStoreReadScope.Open(engine, view, principalId, request.Partition, request.Store);
            var rows = AnalyticsStoreScan.Read(engine, raw, scope, request.Partition, request.Store, request.FromInclusive, request.ToExclusive, work);
            var candidates = rows.Where(row => request.AfterId is null || StringComparer.Ordinal.Compare(row.Id, request.AfterId) > EqualAnalyticsIdentifierOrder).ToArray();
            var more = candidates.Length > request.Limit;
            var page = candidates.Take(request.Limit).Select(row => AnalyticsStoreRowProjection.Project(engine, view, scope, request.Partition, row)).ToImmutableArray();
            var result = new AnalyticsEventsPage(page, more, more ? page.Last().Id : null);
            work.Check();
            return result;
        });
    }

    /// <summary>Captures complete bounded aggregate inputs at one authorized canonical store cut.</summary>
    /// <param name = "principalId">Persisted caller identity.</param>
    /// <param name = "request">Exact original aggregate request.</param>
    /// <param name = "cancellationToken">Original request cancellation.</param>
    /// <returns>Immutable owned inputs; no storage view escapes.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static AnalyticsAggregateSnapshot CaptureAnalyticsAggregate(this DatabaseEngine engine, string principalId, AggregateAnalyticsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(request);
        var work = new ReadExecutionBudget(engine.EvaluationClock, cancellationToken);
        return engine.CaptureAnalyticsAggregateRead(principalId, request, work);
    }

    internal static AnalyticsAggregateSnapshot CaptureAnalyticsAggregateBound(this DatabaseEngine engine, string principalId, AggregateAnalyticsRequest request, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.AttachResourceLifetimeBinding(binding);
        work.RequireResourceLifetimeBinding(binding);
        return engine.CaptureAnalyticsAggregateRead(principalId, request, work);
    }

    internal static AnalyticsAggregateSnapshot CaptureAnalyticsAggregateRead(this DatabaseEngine engine, string principalId, AggregateAnalyticsRequest request, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(request);
        work.Check();
        return engine.Store.Read(raw =>
        {
            var scope = AnalyticsStoreReadScope.Open(engine, work.CreateView(raw), principalId, request.Partition, request.Store);
            AnalyticsAggregateValidation.Require(engine, scope, request);
            var rows = AnalyticsStoreScan.Read(engine, raw, scope, request.Partition, request.Store, request.FromInclusive, request.ToExclusive, work);
            var generation = AnalyticsStoreReadScope.Generation(work.CreateView(raw), request.Partition, request.Store);
            var identity = engine.Store.Identity;
            work.Check();
            return new AnalyticsAggregateSnapshot(request, scope.Principal, scope.Resource, scope.Lifetime, rows, identity.Incarnation, identity.ReadGeneration, generation, engine.Store.Position, work, engine.EvaluationClock);
        });
    }
}
