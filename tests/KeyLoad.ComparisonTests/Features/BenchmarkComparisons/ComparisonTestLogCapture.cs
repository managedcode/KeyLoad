using System.Collections.Concurrent;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class ComparisonTestLogCapture : IAsyncDisposable
{
    private const int MaximumRetainedLines = 150;
    private const string ComparisonResourceName = "comparisons";
    private readonly DistributedApplication application;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentQueue<string> lines = new();
    private readonly ConcurrentDictionary<string, Task> logCaptures = new(StringComparer.Ordinal);
    private readonly Task capture;
    private int stopped;

    public ComparisonTestLogCapture(DistributedApplication application)
    {
        this.application = application;
        var logger = application.Services.GetRequiredService<ResourceLoggerService>();
        capture = Task.Run(() => CaptureResourcesAsync(logger), CancellationToken.None);
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0)
        {
            return;
        }

        await lifetime.CancelAsync();
        await capture;
        await Task.WhenAll(logCaptures.Values);
    }

    public async Task WriteToAsync(string path)
    {
        await StopAsync();
        await File.WriteAllLinesAsync(path, lines, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        lifetime.Dispose();
    }

    private async Task CaptureResourcesAsync(ResourceLoggerService logger)
    {
        try
        {
            await foreach (var change in application.ResourceNotifications.WatchAsync(lifetime.Token))
            {
                if (change.Resource.Name == ComparisonResourceName)
                {
                    _ = logCaptures.GetOrAdd(change.ResourceId, _ => CaptureLogsAsync(logger, change.ResourceId));
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task CaptureLogsAsync(ResourceLoggerService logger, string resourceId)
    {
        try
        {
            await foreach (var batch in logger.WatchAsync(resourceId).WithCancellation(lifetime.Token))
            {
                foreach (var line in batch)
                {
                    RetainLine(line.Content);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    private void RetainLine(string line)
    {
        lines.Enqueue(line);
        while (lines.Count > MaximumRetainedLines)
        {
            _ = lines.TryDequeue(out _);
        }
    }
}
