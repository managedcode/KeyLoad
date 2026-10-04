using System.Collections.Concurrent;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class ComparisonTestLogCapture : IAsyncDisposable
{
    private const int MaximumRetainedLines = 150;
    private const int MaximumNodeLines = 2_000;
    private const int MaximumResourceBytes = 1_048_576;
    private const int MaximumLineBytes = 16_384;
    private const string ResourceLogSuffix = ".log";
    private const string ComparisonResourceName = "comparisons";
    private readonly DistributedApplication application;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, ComparisonResourceLogBuffer> logs;
    private readonly ConcurrentDictionary<string, Task> logCaptures = new(StringComparer.Ordinal);
    private readonly Task capture;
    private readonly ComparisonProgressFile? progress;
    private readonly object stopGate = new();
    private Task? stopping;
    private int disposed;

    public ComparisonTestLogCapture(DistributedApplication application, IEnumerable<string>? selectedResources = null,
        string? progressPath = null)
    {
        this.application = application;
        var names = (selectedResources ?? []).Append(ComparisonResourceName).Distinct(StringComparer.Ordinal).ToArray();
        if (names.Any(name => name.Length is < 1 or > 100 || name.Any(character =>
            !char.IsAsciiLetterOrDigit(character) && character != '-')))
        {
            throw new ArgumentException("Resource log names must be confined native identifiers.", nameof(selectedResources));
        }
        logs = names.ToDictionary(name => name, name => new ComparisonResourceLogBuffer(
            name == ComparisonResourceName ? MaximumRetainedLines : MaximumNodeLines,
            MaximumResourceBytes, MaximumLineBytes), StringComparer.Ordinal);
        var logger = application.Services.GetRequiredService<ResourceLoggerService>();
        progress = progressPath is null ? null : new ComparisonProgressFile(progressPath);
        capture = Task.Run(() => CaptureResourcesAsync(logger), CancellationToken.None);
    }

    public Task StopAsync()
    {
        lock (stopGate)
        {
            if (stopping is null)
            {
                var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                stopping = StopCoreAsync(registered.Task);
                registered.SetResult();
            }
            return stopping;
        }
    }

    private async Task StopCoreAsync(Task registered)
    {
        await registered;
        try
        {
            await lifetime.CancelAsync();
        }
        finally
        {
            try
            {
                await JoinCapturesAsync();
            }
            finally
            {
                await (progress?.StopAsync() ?? Task.CompletedTask);
            }
        }
    }

    private async Task JoinCapturesAsync()
    {
        try
        {
            await capture;
        }
        finally
        {
            await Task.WhenAll(logCaptures.Values);
        }
    }

    internal bool HasCapturedLine(string resource, string marker)
        => logs.TryGetValue(resource, out var buffer)
            && buffer.Snapshot().Any(line => line.Contains(marker, StringComparison.Ordinal));

    public async Task WriteToAsync(string path)
    {
        await StopAsync();
        await File.WriteAllLinesAsync(path, logs[ComparisonResourceName].Snapshot(), CancellationToken.None);
    }

    public async Task WriteResourcesToAsync(string directory)
    {
        await StopAsync();
        foreach (var (name, buffer) in logs.Where(item => item.Key != ComparisonResourceName))
        {
            await File.WriteAllLinesAsync(Path.Combine(directory, name + ResourceLogSuffix), buffer.Snapshot(), CancellationToken.None);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }
        try
        {
            await StopAsync();
        }
        finally
        {
            lifetime.Dispose();
        }
    }

    private async Task CaptureResourcesAsync(ResourceLoggerService logger)
    {
        try
        {
            await foreach (var change in application.ResourceNotifications.WatchAsync(lifetime.Token))
            {
                if (logs.ContainsKey(change.Resource.Name))
                {
                    _ = logCaptures.GetOrAdd(change.ResourceId,
                        _ => CaptureLogsAsync(logger, change.ResourceId, change.Resource.Name));
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task CaptureLogsAsync(ResourceLoggerService logger, string resourceId, string name)
    {
        try
        {
            await foreach (var batch in logger.WatchAsync(resourceId).WithCancellation(lifetime.Token))
            {
                foreach (var line in batch)
                {
                    CaptureLine(name, line.Content);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    private void CaptureLine(string name, string line)
    {
        logs[name].Add(line);
        if (name == ComparisonResourceName && ComparisonProgressLine.TryFromNativeLog(line, out var marker))
        {
            progress?.Observe(marker);
        }
    }
}
