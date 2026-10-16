namespace KeyLoad.Comparisons;

internal sealed class BenchmarkSessionQueryWorkload(BenchmarkSessionContext context)
{
    internal async Task<BenchmarkSessionCaseReport> IndexAsync(CancellationToken token)
    {
        var state = context.State;
        if (!state.Ready)
        {
            return state.Prerequisite(BenchmarkSessionProtocol.Index);
        }
        if (context.Target is not IBenchmarkSessionQueries queries)
        {
            return state.Report(BenchmarkSessionProtocol.Index, BenchmarkSessionProtocol.Failed, BenchmarkSessionProtocol.NativeAdapterSessionIndexLifecycleUnavailable, BenchmarkSessionLimits.NoObservedItems, [], BenchmarkSessionLimits.NoObservedItems);
        }
        var timer = new BenchmarkSessionTimer(context.Clock);
        try
        {
            await queries.BuildSessionIndexesAsync(token).ConfigureAwait(false);
            return state.Report(BenchmarkSessionProtocol.Index, BenchmarkSessionProtocol.Measured, null, BenchmarkSessionLimits.NoObservedItems, [], timer.Elapsed.TotalSeconds);
        }
        catch (BenchmarkSessionNativeCapabilityException error)
        {
            return state.Unavailable(BenchmarkSessionProtocol.Index, error.Message);
        }
        catch (Exception error) when (BenchmarkSessionFailure.IsNonfatal(error))
        {
            return state.Failed(BenchmarkSessionProtocol.Index, ComparisonErrors.Safe(error), timer, []);
        }
    }

    internal Task<BenchmarkSessionCaseReport> ReadAsync(bool random, CancellationToken token)
    {
        var id = random ? BenchmarkSessionProtocol.RandomRead : BenchmarkSessionProtocol.SequentialRead;
        if (!context.State.Ready)
        {
            return Task.FromResult(context.State.Prerequisite(id));
        }
        if (!context.Target.Supports(Scenario.PointRead))
        {
            return Task.FromResult(context.State.Unavailable(id, context.Target.UnsupportedReason));
        }
        return RunLaneAsync(id, BenchmarkSessionProtocol.Read, (client, ordinal, cancellation) => BenchmarkSessionDocumentOperation.ExecuteAsync(client,
            Scenario.PointRead, context.Corpus.CreateDocument(context.StableIdentity(ordinal, random)), cancellation), true, token);
    }

    internal Task<BenchmarkSessionCaseReport> SearchAsync(CancellationToken token)
    {
        if (!context.State.Ready)
        {
            return Task.FromResult(context.State.Prerequisite(BenchmarkSessionProtocol.Search));
        }
        if (context.Target is not IBenchmarkSessionQueries queries)
        {
            return Task.FromResult(context.State.Report(BenchmarkSessionProtocol.Search, BenchmarkSessionProtocol.Failed, BenchmarkSessionProtocol.NativeAdapterSessionQueryLifecycleUnavailable, BenchmarkSessionLimits.NoObservedItems, [], BenchmarkSessionLimits.NoObservedItems));
        }
        return RunLaneAsync(BenchmarkSessionProtocol.Search, BenchmarkSessionProtocol.Search, (_, ordinal, cancellation) => queries.QuerySessionAsync(ordinal,
            BenchmarkSessionLimits.StableMutationGroups * context.Options.Operations, context.Options.Records - BenchmarkSessionLimits.StableMutationGroups * context.Options.Operations, cancellation), false, token);
    }

    private async Task<BenchmarkSessionCaseReport> RunLaneAsync(string id, string name,
        Func<IComparisonSession, int, CancellationToken, Task> operation, bool documentSessions, CancellationToken token)
    {
        var timer = new BenchmarkSessionTimer(context.Clock);
        try
        {
            var lane = await context.Measurement.MeasureAsync(name, context.Options.Operations, context.Options.Concurrency,
                operation, token, openDocumentSessions: documentSessions).ConfigureAwait(false);
            return lane.Measurement.Failures != BenchmarkSessionLimits.NoObservedItems || lane.Measurement.Attempts != context.Options.Operations
                ? context.State.Failed(id, BenchmarkSessionProtocol.SessionNativeOperationAccountingIncomplete, timer, [lane])
                : context.State.Report(id, BenchmarkSessionProtocol.Measured, null, BenchmarkSessionLimits.NoObservedItems, [lane], timer.Elapsed.TotalSeconds);
        }
        catch (Exception error) when (BenchmarkSessionFailure.IsNonfatal(error))
        {
            return context.State.Failed(id, ComparisonErrors.Safe(error), timer, []);
        }
    }
}
