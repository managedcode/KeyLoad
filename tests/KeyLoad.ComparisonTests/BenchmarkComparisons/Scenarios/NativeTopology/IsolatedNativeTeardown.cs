using System.Runtime.ExceptionServices;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedNativeTeardown
{
    private const string LogFile = "runner.log";
    private const string Receipt = "teardown.json";

    internal static Task CompleteAsync(DistributedApplication app, string output, string evidence, string root,
        ContainerResource[] containers, IsolatedNativeOwnedWork work, Exception? primaryFailure)
    {
        var capture = work.Capture;
        Func<Task>? disposeCapture = capture is null ? null : () => work.DisposeCaptureAsync(capture);
        return CompleteCoreAsync(app, capture, disposeCapture, output, evidence, root, containers, work, primaryFailure);
    }

    internal static Task CompleteAsync(DistributedApplication app, ComparisonTestLogCapture? capture,
        string output, string evidence, string root, ContainerResource[] containers, IsolatedNativeOwnedWork work,
        Exception? primaryFailure)
    {
        Func<Task>? disposeCapture = capture is null ? null : () => capture.DisposeAsync().AsTask();
        return CompleteCoreAsync(app, capture, disposeCapture, output, evidence, root, containers, work, primaryFailure);
    }

    private static async Task CompleteCoreAsync(DistributedApplication app, ComparisonTestLogCapture? capture,
        Func<Task>? disposeCapture, string output, string evidence, string root, ContainerResource[] containers,
        IsolatedNativeOwnedWork work, Exception? primaryFailure)
    {
        var failures = new IsolatedNativeTeardownFailures(primaryFailure);
        var options = work.ExecutionOptions;
        var runnerSettled = await IsolatedNativeOriginalTaskSettlement.RunAsync(
            work.StopRunnerAsync, "native-runner", failures, options);
        var collectorSettled = await SettleCollectorAsync(work, failures, options);
        var captureSettled = capture is null || await IsolatedNativeOriginalTaskSettlement.RunAsync(
            capture.StopAsync, "logs-close", failures, options);
        if (capture is not null)
        {
            await IsolatedNativeOriginalTaskSettlement.RunAsync(
                () => capture.WriteToAsync(Path.Combine(evidence, LogFile)), "logs-retain", failures, options);
            await IsolatedNativeOriginalTaskSettlement.RunAsync(
                () => capture.WriteResourcesToAsync(evidence), "node-logs-retain", failures, options);
        }
        await IsolatedNativeOriginalTaskSettlement.RunAsync(() => CopyRawAsync(output, evidence), "raw", failures, options);
        var stopped = await StopAsync(app, failures, options);
        var captureDisposed = disposeCapture is null || await IsolatedNativeOriginalTaskSettlement.RunAsync(
            disposeCapture, "capture-dispose", failures, options);
        var applicationDisposed = await IsolatedNativeOriginalTaskSettlement.RunAsync(
            () => app.DisposeAsync().AsTask(), "app-dispose", failures, options);
        var nativeReportsRetained = stopped && applicationDisposed
            && await IsolatedNativeOriginalTaskSettlement.RunAsync(() => RetainNativeReportsAsync(output, evidence),
                "native-trx-retain", failures, options);
        if (runnerSettled && collectorSettled && captureSettled && captureDisposed && stopped && applicationDisposed && nativeReportsRetained)
        {
            await IsolatedNativeOriginalTaskSettlement.RunAsync(
                () => IsolatedNativeDataCleanup.DeleteAsync(root, containers), "data", failures, options);
        }

        await IsolatedNativeOriginalTaskSettlement.RunAsync(
            () => WriteReceiptAsync(evidence, failures, options), "receipt", failures, options);
        failures.ThrowIfAny();
    }

    internal static async Task SettleOwnerDisposalAsync(Task original, Task disposal)
    {
        var primary = await NativeComparisonFailure.ObserveAsync(original);
        var cleanup = await NativeComparisonFailure.ObserveAsync(disposal);
        if (cleanup is null || ContainsAllFailures(primary, cleanup))
        {
            // A repeated await of the same native completion must not replace its already retained teardown failure.
            return;
        }
        var combined = NativeComparisonFailure.Combine(primary, [cleanup]);
        ExceptionDispatchInfo.Capture(combined!).Throw();
    }

    private static bool ContainsAllFailures(Exception? original, Exception observed)
        => observed is AggregateException aggregate && aggregate.InnerExceptions.Count > 0
            ? aggregate.InnerExceptions.All(error => ContainsAllFailures(original, error))
            : ContainsFailure(original, observed);

    private static bool ContainsFailure(Exception? original, Exception observed)
        => ReferenceEquals(original, observed)
            || original is AggregateException aggregate && aggregate.InnerExceptions.Any(error => ContainsFailure(error, observed));

    private static async Task<bool> SettleCollectorAsync(IsolatedNativeOwnedWork work,
        IsolatedNativeTeardownFailures failures, IOptions<NativeComparisonHarnessOptions> options)
    {
        if (work.Collector is null && work.Observation is null)
        {
            return true;
        }

        if (work.Collector is null || work.Observation is null)
        {
            failures.Record("server-resource", new InvalidOperationException("Server resource task ownership was incomplete."));
            return false;
        }
        var original = work.StartSettlement();
        return await IsolatedNativeOriginalTaskSettlement.RunAsync(() => original, "server-resource", failures, options);
    }

    private static async Task<bool> StopAsync(DistributedApplication app,
        IsolatedNativeTeardownFailures failures, IOptions<NativeComparisonHarnessOptions> options)
    {
        using var cancellation = new CancellationTokenSource();
        return await IsolatedNativeOriginalTaskSettlement.RunAsync(
            () => app.StopAsync(cancellation.Token), "stop", failures, options, cancellation.CancelAsync);
    }

    private static Task RetainNativeReportsAsync(string output, string evidence)
    {
        NativeWorkloadTestReports.Retain(output, evidence);
        return Task.CompletedTask;
    }

    private static Task CopyRawAsync(string output, string evidence)
    {
        Directory.CreateDirectory(evidence);
        if (Path.GetFullPath(output) == Path.GetFullPath(evidence))
        {
            return Task.CompletedTask;
        }
        foreach (var name in new[] { "session-state.json", "session.json", "session-resource-evidence.json" })
        {
            var source = Path.Combine(output, name);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(evidence, name), overwrite: false);
            }
        }
        return Task.CompletedTask;
    }

    private static async Task WriteReceiptAsync(string evidence, IsolatedNativeTeardownFailures failures, IOptions<NativeComparisonHarnessOptions> options)
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
