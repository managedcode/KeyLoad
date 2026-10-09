namespace KeyLoad.Comparisons;

internal sealed partial class DocumentComparisonExecution
{
    private async Task RunWorkerAsync(IDocumentComparisonSession client, int repetition, int worker, int clients,
        DocumentComparisonScenario scenario, DocumentComparisonSchedule schedule, DocumentMeasurementState state,
        ComparisonElapsedMeasurement timer, Task barrier, CancellationTokenSource lifetime)
    {
        var token = lifetime.Token;
        await barrier.WaitAsync(token).ConfigureAwait(false);
        // Fixed disjoint client ranges for ingestion; ordinary schedules retain deterministic striped ownership.
        var perClient = state.Planned / clients;
        var begin = scenario == DocumentComparisonScenario.Ingest ? worker * perClient : worker;
        var end = scenario == DocumentComparisonScenario.Ingest ? (worker + DocumentMeasurementValues.SingleItemCount) * perClient : state.Planned;
        var stride = scenario == DocumentComparisonScenario.Ingest ? DocumentMeasurementValues.SingleItemCount : clients;
        for (var ordinal = begin; ordinal < end; ordinal += stride)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            var work = schedule.At(ordinal);
            state.Begin();
            var started = timer.Elapsed.TotalMilliseconds;
            using var deadline = ComparisonDeadline.Create(execution.OperationTimeout, cancellationToken: token, timeProvider: clock);
            try
            {
                var response = await client.ExecuteAsync(work.Operation, work.Input, deadline.Token).ConfigureAwait(false);
                ScaledComparisonMeasurementExecutor.ValidatePointRead(work.Operation, response, work.Input);
                state.End(timer.Elapsed.TotalMilliseconds - started, success: true, wasCanceled: false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { state.End(timer.Elapsed.TotalMilliseconds - started, false, true); return; }
            catch (Exception error)
            {
                state.End(timer.Elapsed.TotalMilliseconds - started, false, false);
                state.RecordFailure(error);
                if (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
                { await lifetime.CancelAsync().ConfigureAwait(false); throw; }
            }
            try
            {
                nativeProgress?.Invoke(new(repetition, state.Attempts, state.Acknowledged, state.Planned,
                state.Failed, state.Canceled, timer.Elapsed.TotalSeconds));
            }
            catch (Exception) { await lifetime.CancelAsync().ConfigureAwait(false); throw; }
        }
    }

}
