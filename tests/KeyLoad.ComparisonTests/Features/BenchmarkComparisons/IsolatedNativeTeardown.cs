using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedNativeTeardown
{
    private const string LogFile = "runner.log";
    private const string Failure = "IsolatedNativeTeardownFailed";
    private const string Receipt = "teardown.json";
    private const int TimeoutSeconds = 30;

    internal static async Task CompleteAsync(DistributedApplication app, ComparisonTestLogCapture capture,
        string output, string evidence, string root, ContainerResource[] containers, bool primaryFailure)
    {
        var failures = new List<string>();
        await AttemptAsync(() =>
        {
            Directory.CreateDirectory(evidence);
            IsolatedNativeReportAssertions.CopyRawIfPresent(output, evidence);
            return Task.CompletedTask;
        }, "raw", failures);
        await AttemptAsync(() => capture.StopAsync().WaitAsync(TimeSpan.FromSeconds(TimeoutSeconds)), "logs-close", failures);
        await AttemptAsync(() => capture.WriteToAsync(Path.Combine(evidence, LogFile))
            .WaitAsync(TimeSpan.FromSeconds(TimeoutSeconds)), "logs-retain", failures);
        await AttemptAsync(() => capture.WriteResourcesToAsync(evidence)
            .WaitAsync(TimeSpan.FromSeconds(TimeoutSeconds)), "node-logs-retain", failures);
        var stopped = await StopAsync(app, failures);
        await AttemptAsync(() => DisposeAsync(capture), "capture-dispose", failures);
        await AttemptAsync(() => DisposeAsync(app), "app-dispose", failures);
        if (stopped)
        {
            await AttemptAsync(() => IsolatedNativeDataCleanup.DeleteAsync(root, containers), "data", failures);
        }
        await AttemptAsync(() => WriteReceiptAsync(evidence, failures, primaryFailure), "receipt", failures);
        if (failures.Count > 0 && !primaryFailure)
        {
            throw new IOException(Failure + ":" + string.Join(',', failures));
        }
    }

    private static async Task WriteReceiptAsync(string evidence, List<string> failures, bool primaryFailure)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            primaryFailure,
            failedStages = failures.ToArray(),
        });
        await using var stream = new FileStream(Path.Combine(evidence, Receipt), FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await stream.WriteAsync(bytes, timeout.Token);
        await stream.FlushAsync(timeout.Token);
    }

    private static async Task<bool> StopAsync(DistributedApplication app, List<string> failures)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        return await AttemptAsync(() => app.StopAsync(timeout.Token), "stop", failures);
    }

    private static async Task DisposeAsync(IAsyncDisposable resource)
    {
        var disposal = resource.DisposeAsync().AsTask();
        try
        {
            await disposal.WaitAsync(TimeSpan.FromSeconds(TimeoutSeconds));
        }
        catch (Exception)
        {
            _ = disposal.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw new ComparisonFailureException(Failure);
        }
    }

    private static async Task<bool> AttemptAsync(Func<Task> action, string category, List<string> failures)
    {
        try
        {
            await ExecuteAsync(action);
            return true;
        }
        catch (ComparisonFailureException)
        {
            failures.Add(category);
            return false;
        }
    }

    private static async Task ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception)
        {
            throw new ComparisonFailureException(Failure);
        }
    }
}
