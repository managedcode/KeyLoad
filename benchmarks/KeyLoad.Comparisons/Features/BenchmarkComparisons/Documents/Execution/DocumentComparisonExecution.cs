using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

internal sealed partial class DocumentComparisonExecution(IOptions<NativeComparisonExecutionOptions> executionOptions,
    TimeProvider clock, Action<DocumentComparisonProgress>? nativeProgress)

{
    private readonly NativeComparisonExecutionOptions execution = NativeComparisonExecutionOptions.Require(executionOptions).Value;
    internal async Task PrepareRepetitionAsync(Func<int, CancellationToken, Task<IComparisonTarget>> factory,
        DocumentRepetitionOwner run, CancellationToken token)
    {
        var start = clock.GetTimestamp();
        run.Target = await factory(run.Repetition * DocumentMeasurementValues.NamespacesPerRepetition + DocumentMeasurementValues.SingleItemCount, token).ConfigureAwait(false);
        if (run.Target is not IDocumentComparisonTarget native || !DocumentComparisonRunner.RequiredOperations(run.Selection.Scenario).All(run.Target.Supports))
        { run.Unsupported = true; run.Observe(DocumentProtocolText.NativeDocumentLifecycleOrOperationUnavailable); return; }
        await native.InitializeDocumentsAsync(new(run.Corpus, run.Schedule.Initial != DocumentMeasurementValues.NoObservedItems, run.Schedule.MaximumIdentityExclusive), token).ConfigureAwait(false);
        run.Profile = run.Target.Profile;
        for (var index = DocumentMeasurementValues.NoObservedItems; index < run.Selection.Clients; index++)
        {
            using var admission = ComparisonDeadline.Create(execution.OperationTimeout, cancellationToken: token, timeProvider: clock);
            var client = await native.OpenDocumentSessionAsync(admission.Token).ConfigureAwait(false);
            run.Clients.Add(client);
            if (client.ClientIdentity == Guid.Empty || run.Clients.Take(run.Clients.Count - DocumentMeasurementValues.SingleItemCount).Any(existing => existing.ClientIdentity == client.ClientIdentity))
            {
                throw new ComparisonFailureException(DocumentProtocolText.NativeDocumentClientIdentityMismatch);
            }

            run.SingleOperation &= client.SingleOperationTiming;
        }
        run.SetupSeconds = clock.GetElapsedTime(start).TotalSeconds;
        start = clock.GetTimestamp();
        _ = await DocumentComparisonOracle.VerifyAsync(run.Clients[DocumentMeasurementValues.NoObservedItems].ReadDocumentsAsync(token), run.Schedule.FinalDocuments(initialState: true), token).ConfigureAwait(false);
        run.VerificationSeconds += clock.GetElapsedTime(start).TotalSeconds;
    }

    internal async Task MeasureRepetitionAsync(DocumentRepetitionOwner run, CancellationToken token)
    {
        var timer = new ComparisonElapsedMeasurement(clock);
        await using var resources = new ClientResourceSampler(executionOptions, provider: clock);
        using var calls = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        { await RunWorkersAsync(run, timer, calls).ConfigureAwait(false); }
        finally
        {
            timer.Stop();
            run.MeasuredSeconds = timer.Elapsed.TotalSeconds;
            run.Resources = await resources.StopAsync().ConfigureAwait(false);
        }
        var start = clock.GetTimestamp();
        run.Readback = await DocumentComparisonOracle.VerifyAsync(run.Clients[DocumentMeasurementValues.NoObservedItems].ReadDocumentsAsync(token), run.Schedule.FinalDocuments(), token).ConfigureAwait(false);
        await ((IDocumentComparisonTarget)run.Target!).VerifyDocumentCopiesAsync(run.Schedule, token).ConfigureAwait(false);
        run.Profile = run.Target.Profile;
        run.VerificationSeconds += clock.GetElapsedTime(start).TotalSeconds;
    }

    private async Task RunWorkersAsync(DocumentRepetitionOwner run, ComparisonElapsedMeasurement timer, CancellationTokenSource calls)
    {
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workers = new List<Task>();
        for (var worker = DocumentMeasurementValues.NoObservedItems; worker < run.Clients.Count; worker++)
        {
            workers.Add(RunWorkerAsync(run.Clients[worker], run.Repetition, worker, run.Clients.Count,
                run.Selection.Scenario, run.Schedule, run.State, timer, barrier.Task, calls));
        }

        barrier.SetResult();
        var joined = Task.WhenAll(workers);
        try
        { await joined.ConfigureAwait(false); }
        catch (Exception error)
        {
            if (joined.Exception is not null)
            {
                run.Observe(joined.Exception);
            }
            else
            {
                run.Observe(error);
            }

            throw;
        }
    }

    internal async Task SettleRepetitionAsync(DocumentRepetitionOwner run)
    {
        var start = clock.GetTimestamp();
        var settled = await ComparisonSessionCleanup.CloseAndJoinAsync(run.Clients, execution.CleanupTimeout, timeProvider: clock).ConfigureAwait(false);
        foreach (var failure in settled.Failures)
        {
            run.Observe(failure);
        }

        if (settled.ThresholdExpired)
        {
            run.Observe(DocumentProtocolText.DocumentClientCleanupThresholdExpired);
        }

        if (run.Target is not null)
        {
            if (await OpenLoopFailure.ObserveAsync(DisposeTargetAsync(run.Target)).ConfigureAwait(false) is { } failure)
            {
                run.Observe(failure);
            }
        }
        run.CleanupSeconds = clock.GetElapsedTime(start).TotalSeconds;
    }
    private static async Task DisposeTargetAsync(IComparisonTarget target) => await target.DisposeAsync().ConfigureAwait(false);
}
