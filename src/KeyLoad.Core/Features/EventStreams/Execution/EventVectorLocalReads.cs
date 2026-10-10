using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int EmptyVectorSources = 0;
    private const long EmptyVectorResultBytes = 0;
    private const string InvalidVectorSources = "The event vector source selection is invalid.";
    private const string VectorResultExceeded = "The complete event vector result exceeds its bounds.";

    internal ImmutableArray<EventSourcePage> ReadEventVectorSources(string principalId,
        ImmutableArray<ReadEventSourceRequest> requests, PhysicalShardRecord configuredOwner,
        CancellationToken cancellationToken)
        => ReadEventVectorSources(principalId, requests, configuredOwner,
            new ReadExecutionBudget(OperationLimitsOptions, EvaluationClock, cancellationToken));

    internal ImmutableArray<EventSourcePage> ReadEventVectorSources(string principalId,
        ImmutableArray<ReadEventSourceRequest> requests, PhysicalShardRecord configuredOwner,
        ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(configuredOwner);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        budget.ConstrainResultBytes(checked((int)Math.Min(Limits.MaxBatchBytes, Limits.MaxQueryReadBytes)));
        if (requests.IsDefaultOrEmpty || requests.Length > Limits.MaxResults
            || requests.Any(static request => request is null || request.Source is null)
            || requests.Select(static request => request.Source).Distinct().Count() != requests.Length)
        { throw Errors.Fail(ErrorCode.Validation, InvalidVectorSources); }
        return Store.Read(view => ReadEventVectorSources(view, principalId, requests, configuredOwner, budget));
    }

    private ImmutableArray<EventSourcePage> ReadEventVectorSources(IKeyValueView view, string principalId,
        ImmutableArray<ReadEventSourceRequest> requests, PhysicalShardRecord configuredOwner,
        ReadExecutionBudget budget)
    {
        var bounded = budget.CreateView(view);
        var principal = Principal(bounded, principalId, Clock.GetUtcNow());
        foreach (var request in requests)
        {
            budget.Check();
            Authorization.Require(principal, request.Source.Partition, request.Source.Resource,
                SourceReadCapability(request.Source));
            StreamTraversalPlacement(bounded, request.Source.Partition, configuredOwner);
            _ = EventVectorStoredSourceHead(bounded, request.Source);
        }
        var pages = ImmutableArray.CreateBuilder<EventSourcePage>(requests.Length);
        var eventCount = EmptyVectorSources;
        var resultBytes = EmptyVectorResultBytes;
        foreach (var request in requests)
        {
            budget.Check();
            var page = ReadEventSource(view, principalId, request, budget);
            eventCount = checked(eventCount + page.Events.Length);
            resultBytes = checked(resultBytes + budget.MeasureResult(page));
            if (eventCount > Limits.MaxResults || resultBytes > budget.MaximumResultBytes)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, VectorResultExceeded); }
            pages.Add(page);
        }
        var result = pages.MoveToImmutable();
        budget.CheckResult(result);
        return result;
    }
}
