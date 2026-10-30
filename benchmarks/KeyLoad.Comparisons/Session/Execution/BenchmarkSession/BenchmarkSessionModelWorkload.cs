using System.Collections.Immutable;
using System.Globalization;

namespace KeyLoad.Comparisons;

internal sealed class BenchmarkSessionModelWorkload(BenchmarkSessionContext context)
{
    private const string Mismatch = "SessionModelResultMismatch";
    private const int MixedStreams = 2;
    private BenchmarkSessionModelCorpus? corpus;
    private BenchmarkDocument[] queries = [];
    private ImmutableArray<FoundDocument>[] neighbors = [];
    private bool initializationAttempted;
    private bool initialized;
    private int appendedPasses;

    internal async Task<BenchmarkSessionCaseReport> RunAsync(string id, CancellationToken token)
    {
        var scenario = id == BenchmarkSessionModelCases.Prepare
            ? new[] { Scenario.VectorExact, Scenario.QueueCycle, Scenario.StreamAppend }.FirstOrDefault(context.Target.Supports)
            : ScenarioFor(id);
        if (id == BenchmarkSessionModelCases.Prepare
            && !new[] { Scenario.VectorExact, Scenario.QueueCycle, Scenario.StreamAppend }.Any(context.Target.Supports)
            || !context.Target.Supports(scenario))
        {
            return context.State.Unavailable(id, context.Target.UnsupportedReason);
        }
        var timer = new BenchmarkSessionTimer(context.Clock);
        ImmutableArray<BenchmarkSessionLaneReport> retained = [];
        try
        {
            if (id == BenchmarkSessionModelCases.Prepare)
            {
                await InitializeAsync(token).ConfigureAwait(false);
                return new(id, BenchmarkSessionProtocol.Measured, null, BenchmarkSessionLimits.NoObservedItems,
                    context.Target.Supports(Scenario.VectorExact) || context.Target.Supports(Scenario.StreamAppend)
                        ? context.Options.Records : BenchmarkSessionLimits.NoObservedItems,
                    BenchmarkSessionLimits.NoObservedItems, [], timer.Elapsed.TotalSeconds);
            }
            if (!initialized)
            {
                throw new ComparisonFailureException(BenchmarkSessionProtocol.LiveSessionPrerequisiteFailedOrNotExecuted);
            }
            if (id is BenchmarkSessionModelCases.Read or BenchmarkSessionModelCases.Mixed
                && appendedPasses == BenchmarkSessionLimits.NoObservedItems)
            {
                throw new ComparisonFailureException(BenchmarkSessionProtocol.LiveSessionPrerequisiteFailedOrNotExecuted);
            }
            if (id == BenchmarkSessionModelCases.Vector && neighbors.Length == BenchmarkSessionLimits.NoObservedItems)
            {
                queries = corpus!.Queries();
                neighbors = corpus.ExactNeighbors(queries, token);
            }
            retained = id == BenchmarkSessionModelCases.Mixed
                ? await MixedAsync(token).ConfigureAwait(false)
                : [await MeasureAsync(id, scenario, BenchmarkSessionLimits.NoObservedItems, context.Options.Concurrency, token).ConfigureAwait(false)];
            if (retained.Any(lane => lane.Measurement.Failures != BenchmarkSessionLimits.NoObservedItems
                || lane.Measurement.Successes != context.Options.Operations))
            {
                return context.State.Failed(id, Mismatch, timer, retained);
            }
            if (id is BenchmarkSessionModelCases.Append or BenchmarkSessionModelCases.Mixed)
            {
                await VerifyAppendedAsync(id == BenchmarkSessionModelCases.Mixed ? BenchmarkSessionLimits.SingleItemCount
                    : BenchmarkSessionLimits.NoObservedItems, token).ConfigureAwait(false);
                appendedPasses++;
            }
            return Report(id, retained);
        }
        catch (Exception error) when (BenchmarkSessionFailure.IsNonfatal(error))
        {
            return context.State.Failed(id, ComparisonErrors.Safe(error), timer, retained);
        }
    }

    private BenchmarkSessionCaseReport Report(string id, ImmutableArray<BenchmarkSessionLaneReport> lanes)
    {
        var initial = id switch
        {
            BenchmarkSessionModelCases.Queue => BenchmarkSessionLimits.NoObservedItems,
            BenchmarkSessionModelCases.Append or BenchmarkSessionModelCases.Mixed => context.Options.Records
                + (appendedPasses - BenchmarkSessionLimits.SingleItemCount) * context.Options.Operations,
            BenchmarkSessionModelCases.Read => context.Options.Records + appendedPasses * context.Options.Operations,
            _ => context.Options.Records
        };
        var expected = initial + (id is BenchmarkSessionModelCases.Append or BenchmarkSessionModelCases.Mixed
            ? context.Options.Operations : BenchmarkSessionLimits.NoObservedItems);
        return new(id, BenchmarkSessionProtocol.Measured, null, initial, expected,
            context.Options.Operations, lanes, lanes.Max(lane => lane.Measurement.ElapsedSeconds));
    }

    private async Task InitializeAsync(CancellationToken token)
    {
        if (initialized)
        {
            return;
        }
        if (initializationAttempted)
        {
            throw new ComparisonFailureException(BenchmarkSessionProtocol.LiveSessionPrerequisiteFailedOrNotExecuted);
        }
        initializationAttempted = true;
        corpus = new(context.Options);
        await context.Target.InitializeAsync(corpus, token).ConfigureAwait(false);
        initialized = true;
    }

    private async Task<ImmutableArray<BenchmarkSessionLaneReport>> MixedAsync(CancellationToken token)
    {
        var barrier = new BenchmarkSessionStartBarrier(MixedStreams, context.Clock);
        var clients = context.Options.Concurrency / MixedStreams;
        var lanes = await Task.WhenAll(
            MeasureAsync(BenchmarkSessionModelCases.Append, Scenario.StreamAppend, BenchmarkSessionLimits.SingleItemCount, clients, token, barrier),
            MeasureAsync(BenchmarkSessionModelCases.Read, Scenario.StreamRead, BenchmarkSessionLimits.NoObservedItems,
                context.Options.Concurrency - clients, token, barrier)).ConfigureAwait(false);
        if (lanes.Max(lane => lane.FirstStartedMs) >= lanes.Min(lane => lane.LastCompletedMs))
        {
            throw new ComparisonFailureException(BenchmarkSessionProtocol.MixedStreamsDidNotOverlap);
        }
        return [.. lanes];
    }

    private async Task<BenchmarkSessionLaneReport> MeasureAsync(string id, Scenario scenario, int repetition,
        int clients, CancellationToken token, BenchmarkSessionStartBarrier? barrier = null)
    {
        var completed = scenario == Scenario.QueueCycle ? new int[context.Options.Operations] : [];
        var phases = scenario == Scenario.QueueCycle ? new QueueTimings?[context.Options.Operations] : [];
        var lane = await context.Measurement.MeasureAsync(id, context.Options.Operations, clients,
            async (session, ordinal, cancellation) =>
            {
                var input = scenario == Scenario.VectorExact ? queries[ordinal % queries.Length]
                    : corpus!.Input(scenario, repetition, ordinal);
                var result = await session!.ExecuteAsync(scenario, input, cancellation).ConfigureAwait(false);
                Validate(scenario, ordinal, input, result, completed);
                if (scenario == Scenario.QueueCycle)
                {
                    phases[ordinal] = result.Queue ?? throw new ComparisonFailureException(Mismatch);
                }
            }, token, barrier, openDocumentSessions: false, openNativeSessions: true).ConfigureAwait(false);
        if (scenario != Scenario.QueueCycle || lane.Measurement.Failures != BenchmarkSessionLimits.NoObservedItems)
        {
            return lane;
        }
        if (completed.Any(count => count != BenchmarkSessionLimits.SingleItemCount))
        {
            throw new ComparisonFailureException(Mismatch);
        }
        return lane with
        {
            Measurement = lane.Measurement with
            {
                UniqueCompletedMessages = completed.Length,
                Enqueue = ComparisonStatistics.Percentiles(phases.Select(phase => phase!.EnqueueMs)),
                Receive = ComparisonStatistics.Percentiles(phases.Select(phase => phase!.ReceiveMs)),
                Ack = ComparisonStatistics.Percentiles(phases.Select(phase => phase!.AckMs))
            }
        };
    }

    private void Validate(Scenario scenario, int ordinal, BenchmarkDocument input, OperationResult result, int[] completed)
    {
        if (scenario == Scenario.StreamRead && !BenchmarkDataset.SameEvent(result.Event, input))
        {
            throw new ComparisonFailureException(Mismatch);
        }
        if (scenario == Scenario.VectorExact)
        {
            var expected = neighbors[ordinal % queries.Length];
            if (result.Neighbors is not { } actual || actual.Length != expected.Length
                || actual.Where((item, index) => item.Id != expected[index].Id
                    || !BenchmarkDataset.SameJson(item.Json, expected[index].Json)).Any())
            {
                throw new ComparisonFailureException(Mismatch);
            }
        }
        if (scenario == Scenario.QueueCycle)
        {
            var message = result.Message ?? throw new ComparisonFailureException(Mismatch);
            if (message.Id.Length != input.Id.Length || !int.TryParse(message.Id.AsSpan(BenchmarkSessionLimits.SingleItemCount),
                NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                || number < context.Options.Records || number >= context.Options.Records + completed.Length
                || !BenchmarkDataset.SameDocument(message, corpus!.CreateDocument(number))
                || Interlocked.Increment(ref completed[number - context.Options.Records]) != BenchmarkSessionLimits.SingleItemCount)
            {
                throw new ComparisonFailureException(Mismatch);
            }
        }
    }

    private async Task VerifyAppendedAsync(int repetition, CancellationToken token)
    {
        await using var reader = await context.Target.OpenSessionAsync(token).ConfigureAwait(false);
        for (var ordinal = BenchmarkSessionLimits.NoObservedItems; ordinal < context.Options.Operations; ordinal++)
        {
            using var deadline = ComparisonDeadline.Create(context.Options.TimeoutSeconds, context.Clock, token);
            var input = corpus!.Input(Scenario.StreamAppend, repetition, ordinal);
            if (!BenchmarkDataset.SameEvent(await reader.ReadEventAsync(input, deadline.Token).ConfigureAwait(false), input))
            {
                throw new ComparisonFailureException(Mismatch);
            }
        }
    }

    private static Scenario ScenarioFor(string id) => id switch
    {
        BenchmarkSessionModelCases.Vector => Scenario.VectorExact,
        BenchmarkSessionModelCases.Queue => Scenario.QueueCycle,
        BenchmarkSessionModelCases.Append or BenchmarkSessionModelCases.Mixed => Scenario.StreamAppend,
        BenchmarkSessionModelCases.Read => Scenario.StreamRead,
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };
}
