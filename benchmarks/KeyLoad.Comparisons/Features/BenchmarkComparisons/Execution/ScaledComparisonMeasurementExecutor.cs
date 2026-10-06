using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

internal static class ScaledComparisonMeasurementExecutor
{
    private const int RequiredLatencySampleCount = 4_096;

    internal static async Task<ScaledMeasurementResult> MeasureAsync(List<IComparisonSession> sessions, ScaledOperationInputs inputs,
        IComparisonSettings settings, IOptions<NativeComparisonExecutionOptions> executionOptions, CancellationToken token)
    {
        var execution = NativeComparisonExecutionOptions.Require(executionOptions).Value;
        var state = new ScaledMeasurementState(settings.Operations, RequiredLatencySampleCount);
        var timer = Stopwatch.StartNew();
        await using var resources = new ClientResourceSampler(executionOptions);
        var workers = sessions.Select((session, worker) => new ScaledComparisonWorker(session, worker,
            inputs, settings, timer, state, execution.OperationTimeout, token).RunAsync()).ToArray();
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
        const string ScaledPointReadMismatchDetail = "ScaledPointReadMismatch";

        if (scenario == Scenario.PointRead && !BenchmarkDataset.SameDocument(result.Document, input))
        {
            throw new ComparisonFailureException(ScaledPointReadMismatchDetail);
        }
    }
}

internal sealed class ScaledComparisonWorker(IComparisonSession session, int worker, ScaledOperationInputs inputs,
    IComparisonSettings settings, Stopwatch timer, ScaledMeasurementState state, TimeSpan operationTimeout, CancellationToken token)
{
    internal async Task RunAsync()
    {
        const string CancelledToken = "Cancelled";
        const string DeadlineExceededToken = "DeadlineExceeded";

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
            using var deadline = ComparisonDeadline.Create(operationTimeout, token);
            try
            {
                var result = await session.ExecuteAsync(inputs.Scenario, input, deadline.Token).ConfigureAwait(false);
                ScaledComparisonMeasurementExecutor.ValidatePointRead(inputs.Scenario, result, input);
                state.CompleteOperation(operation, worker, started, timer.Elapsed.TotalMilliseconds, input, null, true, false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                state.CompleteOperation(operation, worker, started, timer.Elapsed.TotalMilliseconds, input,
                    CancelledToken, false, false);
                throw;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && deadline.Token.IsCancellationRequested)
            {
                state.CompleteOperation(operation, worker, started, timer.Elapsed.TotalMilliseconds, input, DeadlineExceededToken, false, true);
            }
            catch (Exception error) when (!token.IsCancellationRequested)
            {
                state.CompleteOperation(operation, worker, started, timer.Elapsed.TotalMilliseconds, input,
                    ComparisonErrors.Safe(error), false, false, rejection: true);
            }
        }
    }
}
