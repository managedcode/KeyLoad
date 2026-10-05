using System.Diagnostics;

namespace KeyLoad.Comparisons;

internal static class ScaledComparisonMeasurementExecutor
{
    private const int SampleCapacity = 4_096;

    internal static async Task<ScaledMeasurementResult> MeasureAsync(List<IComparisonSession> sessions,
        ScaledOperationInputs inputs, IComparisonSettings settings, CancellationToken token)
    {
        var state = new ScaledMeasurementState(settings.Operations, SampleCapacity);
        var timer = Stopwatch.StartNew();
        await using var resources = new ClientResourceSampler();
        var workers = sessions.Select((session, worker) => new ScaledComparisonWorker(session, worker,
            inputs, settings, timer, state, token).RunAsync()).ToArray();
        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Task.WhenAll returns only after every original worker and native operation has settled.
        }
        timer.Stop();
        var observedResources = await resources.StopAsync().ConfigureAwait(false);
        return state.Complete(timer.Elapsed.TotalSeconds, observedResources);
    }

    internal static void ValidatePointRead(Scenario scenario, OperationResult result, BenchmarkDocument input)
    {
        if (scenario == Scenario.PointRead && !BenchmarkDataset.SameDocument(result.Document, input))
        {
            throw new ComparisonFailureException("ScaledPointReadMismatch");
        }
    }
}

internal sealed class ScaledComparisonWorker(IComparisonSession session, int worker, ScaledOperationInputs inputs,
    IComparisonSettings settings, Stopwatch timer, ScaledMeasurementState state, CancellationToken token)
{
    internal async Task RunAsync()
    {
        while (!token.IsCancellationRequested)
        {
            var operation = state.Next();
            if (operation >= settings.Operations)
            {
                return;
            }
            state.StartOperation();
            var input = inputs.Create(operation, warmup: false);
            var started = timer.Elapsed.TotalMilliseconds;
            using var deadline = ComparisonDeadline.Create(settings.TimeoutSeconds, token);
            try
            {
                var result = await session.ExecuteAsync(inputs.Scenario, input, deadline.Token).ConfigureAwait(false);
                ScaledComparisonMeasurementExecutor.ValidatePointRead(inputs.Scenario, result, input);
                state.CompleteOperation(operation, worker, started, timer.Elapsed.TotalMilliseconds, input, null, true, false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                state.CompleteOperation(operation, worker, started, timer.Elapsed.TotalMilliseconds, input,
                    "Cancelled", false, false);
                throw;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && deadline.Token.IsCancellationRequested)
            {
                state.CompleteOperation(operation, worker, started, timer.Elapsed.TotalMilliseconds, input, "DeadlineExceeded", false, true);
            }
            catch (Exception error) when (!token.IsCancellationRequested)
            {
                state.CompleteOperation(operation, worker, started, timer.Elapsed.TotalMilliseconds, input,
                    ComparisonErrors.Safe(error), false, false, rejection: true);
            }
        }
    }
}
