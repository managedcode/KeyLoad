using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedNativeTeardown
{
    private const string LogFile = "runner.log";
    private const string Receipt = "teardown.json";
    private const int TimeoutSeconds = 30;

    internal static async Task CompleteAsync(DistributedApplication app, ComparisonTestLogCapture? capture,
        string output, string evidence, string root, ContainerResource[] containers, IsolatedNativeOwnedWork work,
        Exception? primaryFailure)
    {
        var failures = new IsolatedNativeTeardownFailures(primaryFailure);
        var runnerSettled = await IsolatedNativeOriginalTaskSettlement.RunAsync(
            work.StopRunnerAsync, "native-runner", failures);
        var controlSettled = work.CancellationControl is null
            || await IsolatedNativeOriginalTaskSettlement.RunAsync(
                () => work.CancellationControl.DisposeAsync().AsTask(), "open-loop-control", failures);
        var collectorSettled = await SettleCollectorAsync(work, failures);
        var captureSettled = capture is null || await IsolatedNativeOriginalTaskSettlement.RunAsync(
            capture.StopAsync, "logs-close", failures);
        if (capture is not null)
        {
            await IsolatedNativeOriginalTaskSettlement.RunAsync(
                () => capture.WriteToAsync(Path.Combine(evidence, LogFile)), "logs-retain", failures);
            await IsolatedNativeOriginalTaskSettlement.RunAsync(
                () => capture.WriteResourcesToAsync(evidence), "node-logs-retain", failures);
        }
        await IsolatedNativeOriginalTaskSettlement.RunAsync(() => CopyRawAsync(output, evidence), "raw", failures);
        var stopped = await StopAsync(app, failures);
        var captureDisposed = capture is null || await IsolatedNativeOriginalTaskSettlement.RunAsync(
            () => capture.DisposeAsync().AsTask(), "capture-dispose", failures);
        var applicationDisposed = await IsolatedNativeOriginalTaskSettlement.RunAsync(
            () => app.DisposeAsync().AsTask(), "app-dispose", failures);
        if (runnerSettled && controlSettled && collectorSettled && captureSettled && captureDisposed && stopped && applicationDisposed)
        {
            await IsolatedNativeOriginalTaskSettlement.RunAsync(
                () => IsolatedNativeDataCleanup.DeleteAsync(root, containers), "data", failures);
        }

        await IsolatedNativeOriginalTaskSettlement.RunAsync(
            () => WriteReceiptAsync(evidence, failures), "receipt", failures);
        failures.ThrowIfAny();
    }

    private static async Task<bool> SettleCollectorAsync(IsolatedNativeOwnedWork work,
        IsolatedNativeTeardownFailures failures)
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
        return await IsolatedNativeOriginalTaskSettlement.RunAsync(() => original, "server-resource", failures);
    }

    private static async Task<bool> StopAsync(DistributedApplication app,
        IsolatedNativeTeardownFailures failures)
    {
        using var cancellation = new CancellationTokenSource();
        return await IsolatedNativeOriginalTaskSettlement.RunAsync(
            () => app.StopAsync(cancellation.Token), "stop", failures, cancellation.CancelAsync);
    }

    private static Task CopyRawAsync(string output, string evidence)
    {
        Directory.CreateDirectory(evidence);
        IsolatedNativeReportAssertions.CopyRawIfPresent(output, evidence);
        return Task.CompletedTask;
    }

    private static async Task WriteReceiptAsync(string evidence, IsolatedNativeTeardownFailures failures)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        var bytes = failures.CreateReceipt();
        await using var stream = new FileStream(Path.Combine(evidence, Receipt), FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await stream.WriteAsync(bytes, timeout.Token);
        await stream.FlushAsync(timeout.Token);
    }
}
