using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

internal sealed class BenchmarkSessionMixedWorkload(BenchmarkSessionContext context)
{
    internal async Task<BenchmarkSessionCaseReport> RunAsync(CancellationToken token)
    {
        var state = context.State;
        if (!state.Ready)
        {
            return state.Prerequisite(BenchmarkSessionProtocol.Mixed);
        }
        if (!context.Target.Supports(Scenario.DocumentWrite) || !context.Target.Supports(Scenario.DocumentUpdate)
            || !context.Target.Supports(Scenario.DocumentDelete) || !context.Target.Supports(Scenario.PointRead))
        {
            return state.Unavailable(BenchmarkSessionProtocol.Mixed, context.Target.UnsupportedReason);
        }
        var timer = new BenchmarkSessionTimer(context.Clock);
        ImmutableArray<BenchmarkSessionLaneReport> lanes = [];
        try
        {
            lanes = await MeasureStreamsAsync(token).ConfigureAwait(false);
            if (lanes.Any(lane => lane.Measurement.Failures != BenchmarkSessionLimits.NoObservedItems || lane.Measurement.Attempts != context.Options.Operations))
            {
                return state.Failed(BenchmarkSessionProtocol.Mixed, BenchmarkSessionProtocol.MixedAcknowledgementsIncomplete, timer, lanes);
            }
            if (lanes.Max(lane => lane.FirstStartedMs) >= lanes.Min(lane => lane.LastCompletedMs))
            {
                return state.Failed(BenchmarkSessionProtocol.Mixed, BenchmarkSessionProtocol.MixedStreamsDidNotOverlap, timer, lanes);
            }
            state.Mixed = true;
            var proof = await context.Verification.VerifyAsync(true, token).ConfigureAwait(false);
            return context.Target is IBenchmarkSessionQueries
                ? state.Report(BenchmarkSessionProtocol.Mixed, BenchmarkSessionProtocol.Measured, null, proof.Records, lanes, lanes.Max(lane => lane.Measurement.ElapsedSeconds))
                : state.Report(BenchmarkSessionProtocol.Mixed, BenchmarkSessionProtocol.Failed, BenchmarkSessionProtocol.AdapterSessionQueryLifecycleUnavailable, proof.Records, lanes, lanes.Max(lane => lane.Measurement.ElapsedSeconds));
        }
        catch (Exception error) when (BenchmarkSessionFailure.IsNonfatal(error))
        {
            return state.Failed(BenchmarkSessionProtocol.Mixed, ComparisonErrors.Safe(error), timer, lanes);
        }
    }

    private async Task<ImmutableArray<BenchmarkSessionLaneReport>> MeasureStreamsAsync(CancellationToken token)
    {
        var options = context.Options;
        var streams = new List<(string Id, Func<IComparisonSession?, int, CancellationToken, Task> Operation)>
        {
            (BenchmarkSessionProtocol.Create, (client, ordinal, cancellation) => BenchmarkSessionDocumentOperation.ExecuteAsync(client, Scenario.DocumentWrite, context.Corpus.CreateDocument(options.Records + ordinal), cancellation)),
            (BenchmarkSessionProtocol.Delete, (client, ordinal, cancellation) => BenchmarkSessionDocumentOperation.ExecuteAsync(client, Scenario.DocumentDelete, context.Corpus.CreateDocument(options.Operations + ordinal), cancellation)),
            (BenchmarkSessionProtocol.Update, (client, ordinal, cancellation) => BenchmarkSessionDocumentOperation.ExecuteAsync(client, Scenario.DocumentUpdate, context.Corpus.Replacement(ordinal), cancellation)),
            (BenchmarkSessionProtocol.Read, (client, ordinal, cancellation) => BenchmarkSessionDocumentOperation.ExecuteAsync(client, Scenario.PointRead, context.Corpus.CreateDocument(context.StableIdentity(ordinal, true)), cancellation))
        };
        if (context.Target is IBenchmarkSessionQueries queries)
        {
            streams.Add((BenchmarkSessionProtocol.Search, (_, ordinal, cancellation) => queries.QuerySessionAsync(ordinal,
                BenchmarkSessionLimits.StableMutationGroups * options.Operations,
                options.Records - BenchmarkSessionLimits.StableMutationGroups * options.Operations, cancellation)));
        }
        if (options.Concurrency < streams.Count)
        {
            throw new InvalidOperationException(BenchmarkSessionProtocol.InsufficientSessionClientsForMixedStreams);
        }
        var barrier = new BenchmarkSessionStartBarrier(streams.Count, context.Clock);
        var tasks = streams.Select((stream, index) => context.Measurement.MeasureAsync(stream.Id, options.Operations,
            options.Concurrency / streams.Count + (index < options.Concurrency % streams.Count ? BenchmarkSessionLimits.SingleItemCount : BenchmarkSessionLimits.NoObservedItems),
            stream.Operation, token, barrier, stream.Id != BenchmarkSessionProtocol.Search));
        return (await Task.WhenAll(tasks).ConfigureAwait(false)).ToImmutableArray();
    }
}
