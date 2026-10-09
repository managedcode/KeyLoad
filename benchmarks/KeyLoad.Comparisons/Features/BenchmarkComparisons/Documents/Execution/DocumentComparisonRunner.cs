using System.Collections.Immutable;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>One common native pure, mixed and configure-only ingestion runner; each repetition owns fresh namespaces.</summary>
public sealed class DocumentComparisonRunner(IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider? provider = null, Action<DocumentComparisonProgress>? nativeProgress = null)
{
    private readonly NativeComparisonExecutionOptions execution = NativeComparisonExecutionOptions.Require(executionOptions).Value;
    private readonly TimeProvider clock = provider ?? TimeProvider.System;

    /// <summary>Runs all three canonical repetitions. Factory indexes 2*r and 2*r+1 own warmup and measurement namespaces.</summary>
    public async Task<DocumentComparisonReport> RunAsync(Func<int, CancellationToken, Task<IComparisonTarget>> targetFactory,
        DocumentComparisonSelection selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetFactory);
        ArgumentNullException.ThrowIfNull(selection);
        selection.Validate();
        if (execution.OperationTimeout != TimeSpan.FromSeconds(DocumentComparisonContract.Current.TimeoutSeconds))
        {
            throw new InvalidOperationException(DocumentProtocolText.DocumentOperationDeadlineMismatch);
        }

        var results = new List<DocumentComparisonRepetition>();
        for (var repetition = DocumentMeasurementValues.NoObservedItems; repetition < DocumentComparisonContract.Current.Repetitions; repetition++)
        {
            results.Add(await RunRepetitionAsync(targetFactory, selection, repetition, selection.Operations,
                selection.Scenario == DocumentComparisonScenario.Ingest ? DocumentMeasurementValues.NoObservedItems : selection.DatasetRecords, cancellationToken).ConfigureAwait(false));
        }

        return Report(selection, results);
    }

    /// <summary>Executes a small explicit development-only native flow; its noncanonical counts can never qualify a cohort.</summary>
    internal async Task<DocumentComparisonReport> RunDevelopmentAsync(Func<int, CancellationToken, Task<IComparisonTarget>> factory,
        DocumentComparisonSelection selection, int records, int operations, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(selection);
        selection.Validate();
        if (records is < DocumentMeasurementValues.DevelopmentMinimumRecords or > DocumentMeasurementValues.DevelopmentMaximumRecords || operations is < DocumentMeasurementValues.ReadUpdate95CycleLength or > DocumentMeasurementValues.DevelopmentMaximumOperations || operations % DocumentMeasurementValues.ReadUpdate95CycleLength != DocumentMeasurementValues.NoObservedItems || records < operations)
        {
            throw new ArgumentOutOfRangeException(nameof(records));
        }

        if (selection.Scenario == DocumentComparisonScenario.Ingest && (operations < selection.Clients
            || operations % selection.Clients != DocumentMeasurementValues.NoObservedItems))
        {
            throw new ArgumentOutOfRangeException(nameof(operations));
        }

        var result = await RunRepetitionAsync(factory, selection, DocumentMeasurementValues.NoObservedItems, operations,
            selection.Scenario == DocumentComparisonScenario.Ingest ? DocumentMeasurementValues.NoObservedItems : records, token).ConfigureAwait(false);
        return new(DocumentMeasurementValues.DocumentSchemaVersion, DocumentProtocolText.DocumentV1Development, selection, result.Status, false, [result with { Qualified = false }]);
    }

    private static DocumentComparisonReport Report(DocumentComparisonSelection selection, List<DocumentComparisonRepetition> results)
    {
        var qualified = results.All(result => result.Qualified);
        var status = qualified ? DocumentProtocolText.Measured : results.All(result => result.Status == DocumentProtocolText.Unsupported) ? DocumentProtocolText.Unsupported : DocumentProtocolText.Failed;
        return new(DocumentMeasurementValues.DocumentSchemaVersion, DocumentComparisonContract.Current.Family, selection, status, qualified, results.ToImmutableArray());
    }

    private async Task<DocumentComparisonRepetition> RunRepetitionAsync(Func<int, CancellationToken, Task<IComparisonTarget>> factory,
        DocumentComparisonSelection selection, int repetition, int operations, int initialRecords, CancellationToken token)
    {
        var run = new DocumentRepetitionOwner(selection, repetition, operations, initialRecords);
        var phases = new DocumentComparisonExecution(executionOptions, clock, nativeProgress);
        var error = await OpenLoopFailure.ObserveAsync(ExecuteRepetitionAsync()).ConfigureAwait(false);
        if (error is NotSupportedException)
        { run.Unsupported = true; run.Observe(DocumentProtocolText.NativeDocumentLifecycleOrOperationUnavailable); }
        else if (error is not null)
        {
            run.Observe(error);
        }

        await phases.SettleRepetitionAsync(run).ConfigureAwait(false);
        return run.Complete();
        async Task ExecuteRepetitionAsync()
        {
            token.ThrowIfCancellationRequested();
            var warmupStart = clock.GetTimestamp();
            await new DocumentWarmupExecution(executionOptions, clock).RunWarmupAsync(factory, selection,
                repetition * DocumentMeasurementValues.NamespacesPerRepetition, token).ConfigureAwait(false);
            run.WarmupSeconds = clock.GetElapsedTime(warmupStart).TotalSeconds;
            await phases.PrepareRepetitionAsync(factory, run, token).ConfigureAwait(false);
            if (!run.Unsupported)
            {
                await phases.MeasureRepetitionAsync(run, token).ConfigureAwait(false);
            }
        }

    }

    internal static IEnumerable<Scenario> RequiredOperations(DocumentComparisonScenario scenario) => scenario switch
    {
        DocumentComparisonScenario.SequentialRead or DocumentComparisonScenario.RandomRead => [Scenario.PointRead],
        DocumentComparisonScenario.Create or DocumentComparisonScenario.Ingest => [Scenario.DocumentWrite],
        DocumentComparisonScenario.Update => [Scenario.DocumentUpdate],
        DocumentComparisonScenario.Delete => [Scenario.DocumentDelete],
        DocumentComparisonScenario.ReadUpdate50 or DocumentComparisonScenario.ReadUpdate95 => [Scenario.PointRead, Scenario.DocumentUpdate],
        _ => [Scenario.PointRead, Scenario.DocumentWrite, Scenario.DocumentUpdate, Scenario.DocumentDelete]
    };
}
