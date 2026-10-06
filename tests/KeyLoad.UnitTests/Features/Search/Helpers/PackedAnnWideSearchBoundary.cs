using System.Diagnostics;
using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal readonly record struct PackedAnnStartedSearch(Task<AnnSearchResult> Search, AnnWorkBudget Budget);

internal static class PackedAnnWideSearchBoundary
{
    private const int SearchLimit = 1_000;
    private const int PollMilliseconds = 1;
    private const int StartTimeoutSeconds = 5;
    private const int ObserverTimeoutSeconds = 15;

    internal static async Task AssertCancellationAsync(PackedAnnIndex index, float[] query, DatabaseLimits limits)
    {
        var cancellation = new CancellationTokenSource();
        try
        {
            var run = await StartAsync(index, query, cancellation, limits);
            var observer = CancelAfterEdgeAsync(run, cancellation);
            try
            {
                await Assert.That(await observer).IsGreaterThan(0);
                await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => run.Search);
                await Assert.That(run.Budget.WorkUnits).IsGreaterThan(0);
                await Assert.That(run.Budget.EdgeVisits).IsGreaterThan(0);
            }
            finally
            {
                await SettleAsync(run.Search, observer, cancellation);
            }
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    internal static Task AssertOneSecondDeadlineAsync(PackedAnnIndex index, float[] query)
        => PackedAnnDeadlineBoundary.AssertOneSecondDeadlineAsync(index, query, SearchLimit);

    internal static async Task<PackedAnnStartedSearch> StartAsync(PackedAnnIndex index,
        float[] query, CancellationTokenSource request, DatabaseLimits limits)
    {
        var ready = new TaskCompletionSource<AnnWorkBudget>(TaskCreationOptions.RunContinuationsAsynchronously);
        var search = Task.Run(() =>
        {
            var budget = new AnnWorkBudget(new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(limits), TimeProvider.System, request.Token),
                PackedAnnIndexTestSupport.GenerousWorkLimit);
            ready.SetResult(budget);
            return index.Search(query, SearchLimit, null, budget);
        });
        try
        {
            var budget = await ready.Task.WaitAsync(TimeSpan.FromSeconds(StartTimeoutSeconds));
            return new(search, budget);
        }
        catch (TimeoutException)
        {
            await request.CancelAsync();
            await SettleSearchAsync(search);
            throw;
        }
    }

    internal static async Task<long> CancelAfterEdgeAsync(PackedAnnStartedSearch run,
        CancellationTokenSource cancellation)
    {
        var timer = Stopwatch.StartNew();
        while (!run.Search.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(ObserverTimeoutSeconds))
        {
            var edges = run.Budget.EdgeVisits;
            if (edges > 0)
            {
                await cancellation.CancelAsync();
                return edges;
            }
            await Task.Delay(PollMilliseconds);
        }
        return run.Budget.EdgeVisits;
    }

    internal static async Task SettleAsync(Task<AnnSearchResult> search,
        Task<long> observer, CancellationTokenSource cancellation)
    {
        await cancellation.CancelAsync();
        await Task.WhenAll(SettleSearchAsync(search), observer);
    }

    private static async Task SettleSearchAsync(Task<AnnSearchResult> search)
    {
        try
        {
            await search;
        }
        catch (OperationCanceledException)
        {
        }
        catch (KeyLoadException)
        {
        }
    }
}
