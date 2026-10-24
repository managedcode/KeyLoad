using System.Runtime.ExceptionServices;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.BenchmarkOrchestration;

internal static class IsolatedTeardown
{
    private const string ExecutionRunnerValue = "execution-runner";
    private const string LogsCloseValue = "logs-close";
    private const string LogsRetainValue = "logs-retain";
    private const string NodeLogsRetainValue = "node-logs-retain";
    private const string RawReportStage = "raw";
    private const string CaptureDisposeValue = "capture-dispose";
    private const string AppDisposeValue = "app-dispose";
    private const string ExecutionTrxRetainValue = "execution-trx-retain";
    private const string DataStage = "data";
    private const string ReceiptStage = "receipt";
    private const int EmptyCount = 0;
    private const string ServerResourceValue = "server-resource";
    private const string ServerResourceTaskOwnershipWasIncompleteMessage = "Server resource task ownership was incomplete.";
    private const string StopStage = "stop";
    private const string SessionStateJsonFileName = "session-state.json";
    private const string SessionJsonFileName = "session.json";
    private const string SessionResourceEvidenceJsonFileName = "session-resource-evidence.json";

    private const string LogFile = "runner.log";
    private const string Receipt = "teardown.json";

    internal static Task CompleteAsync(DistributedApplication app, string output, string evidence, string root,
        ContainerResource[] containers, IsolatedOwnedWork work, Exception? primaryFailure)
    {
        var capture = work.Capture;
        Func<Task>? disposeCapture = capture is null ? null : () => work.DisposeCaptureAsync(capture);
        return CompleteCoreAsync(app, capture, disposeCapture, output, evidence, root, containers, work, primaryFailure);
    }

    internal static Task CompleteAsync(DistributedApplication app, ComparisonTestLogCapture? capture,
        string output, string evidence, string root, ContainerResource[] containers, IsolatedOwnedWork work,
        Exception? primaryFailure)
    {
        Func<Task>? disposeCapture = capture is null ? null : () => capture.DisposeAsync().AsTask();
        return CompleteCoreAsync(app, capture, disposeCapture, output, evidence, root, containers, work, primaryFailure);
    }

    private static async Task CompleteCoreAsync(DistributedApplication app, ComparisonTestLogCapture? capture,
        Func<Task>? disposeCapture, string output, string evidence, string root, ContainerResource[] containers,
        IsolatedOwnedWork work, Exception? primaryFailure)
    {
        var failures = new IsolatedTeardownFailures(primaryFailure);
        var options = work.ExecutionOptions;
        var runnerSettled = await IsolatedOriginalTaskSettlement.RunAsync(
            work.StopRunnerAsync, ExecutionRunnerValue, failures, options);
        var collectorSettled = await SettleCollectorAsync(work, failures, options);
        var captureSettled = capture is null || await IsolatedOriginalTaskSettlement.RunAsync(
            capture.StopAsync, LogsCloseValue, failures, options);
        if (capture is not null)
        {
            await IsolatedOriginalTaskSettlement.RunAsync(
                () => capture.WriteToAsync(Path.Combine(evidence, LogFile)), LogsRetainValue, failures, options);
            await IsolatedOriginalTaskSettlement.RunAsync(
                () => capture.WriteResourcesToAsync(evidence), NodeLogsRetainValue, failures, options);
        }
        await IsolatedOriginalTaskSettlement.RunAsync(() => CopyRawAsync(output, evidence), RawReportStage, failures, options);
        var stopped = await StopAsync(app, failures, options);
        var captureDisposed = disposeCapture is null || await IsolatedOriginalTaskSettlement.RunAsync(
            disposeCapture, CaptureDisposeValue, failures, options);
        var applicationDisposed = await IsolatedOriginalTaskSettlement.RunAsync(
            () => app.DisposeAsync().AsTask(), AppDisposeValue, failures, options);
        var reportsRetained = stopped && applicationDisposed
            && await IsolatedOriginalTaskSettlement.RunAsync(() => RetainReportsAsync(output, evidence),
                ExecutionTrxRetainValue, failures, options);
        if (runnerSettled && collectorSettled && captureSettled && captureDisposed && stopped && applicationDisposed && reportsRetained)
        {
            await IsolatedOriginalTaskSettlement.RunAsync(
                () => IsolatedDataCleanup.DeleteAsync(root, containers), DataStage, failures, options);
        }

        await IsolatedOriginalTaskSettlement.RunAsync(
            () => WriteReceiptAsync(evidence, failures, options), ReceiptStage, failures, options);
        failures.ThrowIfAny();
    }

    internal static async Task SettleOwnerDisposalAsync(Task original, Task disposal)
    {
        var primary = await RuntimeComparisonFailure.ObserveAsync(original);
        var cleanup = await RuntimeComparisonFailure.ObserveAsync(disposal);
        if (cleanup is null || ContainsAllFailures(primary, cleanup))
        {
            // A repeated await of the same execution completion must not replace its already retained teardown failure.
            return;
        }
        var combined = RuntimeComparisonFailure.Combine(primary, [cleanup]);
        ExceptionDispatchInfo.Capture(combined!).Throw();
    }

    private static bool ContainsAllFailures(Exception? original, Exception observed)
        => observed is AggregateException aggregate && aggregate.InnerExceptions.Count > EmptyCount
            ? aggregate.InnerExceptions.All(error => ContainsAllFailures(original, error))
            : ContainsFailure(original, observed);

    private static bool ContainsFailure(Exception? original, Exception observed)
        => ReferenceEquals(original, observed)
            || original is AggregateException aggregate && aggregate.InnerExceptions.Any(error => ContainsFailure(error, observed));

    private static async Task<bool> SettleCollectorAsync(IsolatedOwnedWork work,
        IsolatedTeardownFailures failures, IOptions<ComparisonHarnessOptions> options)
    {
        if (work.Collector is null && work.Observation is null)
        {
            return true;
        }

        if (work.Collector is null || work.Observation is null)
        {
            failures.Record(ServerResourceValue, new InvalidOperationException(ServerResourceTaskOwnershipWasIncompleteMessage));
            return false;
        }
        var original = work.StartSettlement();
        return await IsolatedOriginalTaskSettlement.RunAsync(() => original, ServerResourceValue, failures, options);
    }

    private static async Task<bool> StopAsync(DistributedApplication app,
        IsolatedTeardownFailures failures, IOptions<ComparisonHarnessOptions> options)
    {
        using var cancellation = new CancellationTokenSource();
        return await IsolatedOriginalTaskSettlement.RunAsync(
            () => app.StopAsync(cancellation.Token), StopStage, failures, options, cancellation.CancelAsync);
    }

    private static Task RetainReportsAsync(string output, string evidence)
    {
        WorkloadTestReports.Retain(output, evidence);
        return Task.CompletedTask;
    }

    private static Task CopyRawAsync(string output, string evidence)
    {
        Directory.CreateDirectory(evidence);
        if (Path.GetFullPath(output) == Path.GetFullPath(evidence))
        {
            return Task.CompletedTask;
        }
        foreach (var name in new[] { SessionStateJsonFileName, SessionJsonFileName, SessionResourceEvidenceJsonFileName })
        {
            var source = Path.Combine(output, name);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(evidence, name), overwrite: false);
            }
        }
        return Task.CompletedTask;
    }

    private static async Task WriteReceiptAsync(string evidence, IsolatedTeardownFailures failures, IOptions<ComparisonHarnessOptions> options)
    {
        var execution = options.Value;
        using var timeout = new CancellationTokenSource(execution.TeardownReceiptTimeout, TimeProvider.System);
        var bytes = failures.CreateReceipt();
        await using var stream = new FileStream(Path.Combine(evidence, Receipt), FileMode.CreateNew,
            FileAccess.Write, FileShare.None, execution.TeardownReceiptFileBufferBytes, FileOptions.Asynchronous);
        await stream.WriteAsync(bytes, timeout.Token);
        await stream.FlushAsync(timeout.Token);
    }
}
