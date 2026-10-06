using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedNativeTeardown
{
    private const string LogFile = "runner.log";
    private const string Receipt = "teardown.json";

    internal static async Task CompleteAsync(DistributedApplication app, ComparisonTestLogCapture? capture,
        string output, string evidence, string root, ContainerResource[] containers, IsolatedNativeOwnedWork work,
        Exception? primaryFailure)
    {
        var failures = new IsolatedNativeTeardownFailures(primaryFailure);
        var options = work.ExecutionOptions;
        var runnerSettled = await IsolatedNativeOriginalTaskSettlement.RunAsync(
            work.StopRunnerAsync, "native-runner", failures, options);
        var controlSettled = work.CancellationControl is null
            || await IsolatedNativeOriginalTaskSettlement.RunAsync(
                () => work.CancellationControl.DisposeAsync().AsTask(), "open-loop-control", failures, options);
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
        var captureDisposed = capture is null || await IsolatedNativeOriginalTaskSettlement.RunAsync(
            () => capture.DisposeAsync().AsTask(), "capture-dispose", failures, options);
        var applicationDisposed = await IsolatedNativeOriginalTaskSettlement.RunAsync(
            () => app.DisposeAsync().AsTask(), "app-dispose", failures, options);
        if (runnerSettled && controlSettled && collectorSettled && captureSettled && captureDisposed && stopped && applicationDisposed)
        {
            await IsolatedNativeOriginalTaskSettlement.RunAsync(
                () => IsolatedNativeDataCleanup.DeleteAsync(root, containers), "data", failures, options);
        }

        await IsolatedNativeOriginalTaskSettlement.RunAsync(
            () => WriteReceiptAsync(evidence, failures, options), "receipt", failures, options);
        failures.ThrowIfAny();
    }

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

    private static Task CopyRawAsync(string output, string evidence)
    {
        Directory.CreateDirectory(evidence);
        IsolatedNativeReportAssertions.CopyRawIfPresent(output, evidence);
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
