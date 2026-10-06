using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using KeyLoad.UnitTests.Features.TestInfrastructure;

namespace KeyLoad.UnitTests.Features.Search;

internal readonly record struct PackedAnnDeadlineSearch(
    Task<AnnSearchResult> Search,
    AnnWorkBudget Budget,
    PackedAnnDeadlineObservation Observation);

internal sealed class PackedAnnDeadlineObservation
{
    internal TestElapsedClock StartedTimestamp { get; private set; } = null!;
    internal int CompletedSearches { get; private set; }
    internal long FailedInvocationEdgeVisits { get; private set; }
    internal AnnSearchResult? LastResult { get; private set; }
    internal long LastWorkDelta { get; private set; }
    internal long LastDistanceDelta { get; private set; }
    internal long LastEdgeDelta { get; private set; }
    internal bool SuccessfulDeltasMatch { get; private set; } = true;

    internal void Start(TestElapsedClock timestamp) => StartedTimestamp = timestamp;

    internal void RecordSuccess(AnnSearchResult result, AnnWorkBudget budget,
        long workBefore, long distancesBefore, long edgesBefore)
    {
        CompletedSearches++;
        LastResult = result;
        LastWorkDelta = budget.WorkUnits - workBefore;
        LastDistanceDelta = budget.DistanceEvaluations - distancesBefore;
        LastEdgeDelta = budget.EdgeVisits - edgesBefore;
        SuccessfulDeltasMatch &= result.WorkUnits == LastWorkDelta
            && result.DistanceEvaluations == LastDistanceDelta
            && result.EdgeVisits == LastEdgeDelta;
    }

    internal void RecordDeadlineFailure(long edgeDelta) => FailedInvocationEdgeVisits = edgeDelta;
}

internal static class PackedAnnDeadlineBoundary
{
    private const int QueryDeadlineSeconds = 1;
    private const int StartupTimeoutSeconds = 5;
    private const int CompletionTimeoutSeconds = 15;

    internal static async Task AssertOneSecondDeadlineAsync(
        PackedAnnIndex index, float[] query, int searchLimit)
    {
        using var cancellation = new CancellationTokenSource();
        var limits = new DatabaseLimits { QueryDeadlineSeconds = QueryDeadlineSeconds };
        var run = await StartAsync(index, query, searchLimit, limits, cancellation);
        KeyLoadException? expectedFailure = null;
        try
        {
            var failure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(
                () => run.Search.WaitAsync(TimeSpan.FromSeconds(CompletionTimeoutSeconds), TimeProvider.System)))!;
            expectedFailure = failure;
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            if (run.Observation.CompletedSearches > 0)
            {
                await Assert.That(run.Observation.LastResult).IsNotNull();
                await AssertLastDeltasAsync(run.Observation);
                await Assert.That(run.Observation.SuccessfulDeltasMatch).IsTrue();
            }
            else
            {
                await Assert.That(run.Observation.LastResult).IsNull();
            }
            await Assert.That(run.Observation.FailedInvocationEdgeVisits).IsGreaterThan(0);
            await Assert.That(run.Budget.WorkUnits).IsGreaterThan(0);
            await Assert.That(run.Budget.EdgeVisits).IsGreaterThan(0);
            await Assert.That(run.Observation.StartedTimestamp.Elapsed)
                .IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(QueryDeadlineSeconds));
        }
        catch (Exception assertionFailure)
        {
            try
            {
                await SettleAsync(run.Search, cancellation, expectedFailure);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(assertionFailure, cleanupFailure);
            }
            throw;
        }
        await SettleAsync(run.Search, cancellation, expectedFailure);
    }

    private static async Task AssertLastDeltasAsync(PackedAnnDeadlineObservation observation)
    {
        var result = observation.LastResult!;
        await Assert.That(observation.LastWorkDelta).IsEqualTo(result.WorkUnits);
        await Assert.That(observation.LastDistanceDelta).IsEqualTo(result.DistanceEvaluations);
        await Assert.That(observation.LastEdgeDelta).IsEqualTo(result.EdgeVisits);
    }

    private static async Task<PackedAnnDeadlineSearch> StartAsync(PackedAnnIndex index, float[] query,
        int searchLimit, DatabaseLimits limits, CancellationTokenSource cancellation)
    {
        var ready = new TaskCompletionSource<AnnWorkBudget>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observation = new PackedAnnDeadlineObservation();
        var search = Task.Run(() => SearchUntilDeadline(index, query, searchLimit,
            limits, ready, observation, cancellation.Token));
        try
        {
            var budget = await ready.Task.WaitAsync(TimeSpan.FromSeconds(StartupTimeoutSeconds), TimeProvider.System);
            return new(search, budget, observation);
        }
        catch (TimeoutException startupFailure)
        {
            try
            {
                await StopAndSettleUnstartedAsync(search, cancellation);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(startupFailure, cleanupFailure);
            }
            throw;
        }
    }

    private static AnnSearchResult SearchUntilDeadline(PackedAnnIndex index, float[] query,
        int searchLimit, DatabaseLimits limits, TaskCompletionSource<AnnWorkBudget> ready,
        PackedAnnDeadlineObservation observation, CancellationToken cancellationToken)
    {
        var started = new TestElapsedClock(TimeProvider.System);
        var budget = new AnnWorkBudget(
            new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(limits), started.Provider, cancellationToken),
            PackedAnnIndexTestSupport.GenerousWorkLimit);
        observation.Start(started);
        ready.SetResult(budget);
        while (true)
        {
            var workBefore = budget.WorkUnits;
            var distancesBefore = budget.DistanceEvaluations;
            var edgesBefore = budget.EdgeVisits;
            try
            {
                var result = index.Search(query, searchLimit, null, budget);
                observation.RecordSuccess(result, budget, workBefore, distancesBefore, edgesBefore);
            }
            catch (KeyLoadException error) when (error.Code == ErrorCode.BudgetExceeded)
            {
                observation.RecordDeadlineFailure(budget.EdgeVisits - edgesBefore);
                throw;
            }
        }
    }

    private static async Task SettleAsync(Task<AnnSearchResult> search,
        CancellationTokenSource cancellation, KeyLoadException? observedFailure)
    {
        var cleanupRequested = !search.IsCompleted;
        if (cleanupRequested)
        {
            try
            {
                await cancellation.CancelAsync();
            }
            catch (Exception cancellationFailure)
            {
                try
                {
                    await JoinSearchAsync(search, cancellation, observedFailure);
                }
                catch (Exception joinFailure)
                {
                    throw new AggregateException(cancellationFailure, joinFailure);
                }
                throw;
            }
        }
        await JoinSearchAsync(search, cancellation, observedFailure);
    }

    private static async Task JoinSearchAsync(Task<AnnSearchResult> search,
        CancellationTokenSource cancellation, KeyLoadException? observedFailure)
    {
        try
        {
            await search.WaitAsync(TimeSpan.FromSeconds(CompletionTimeoutSeconds), TimeProvider.System, CancellationToken.None);
        }
        catch (KeyLoadException error) when (ReferenceEquals(error, observedFailure))
        {
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }

    private static async Task StopAndSettleUnstartedAsync(Task<AnnSearchResult> search,
        CancellationTokenSource cancellation)
    {
        try
        {
            await cancellation.CancelAsync();
        }
        catch (Exception cancellationFailure)
        {
            try
            {
                await JoinUnstartedWorkerAsync(search, cancellation);
            }
            catch (Exception joinFailure)
            {
                throw new AggregateException(cancellationFailure, joinFailure);
            }
            throw;
        }
        await JoinUnstartedWorkerAsync(search, cancellation);
    }

    private static async Task JoinUnstartedWorkerAsync(Task<AnnSearchResult> search,
        CancellationTokenSource cancellation)
    {
        try
        {
            await search.WaitAsync(TimeSpan.FromSeconds(CompletionTimeoutSeconds), TimeProvider.System, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }
}
