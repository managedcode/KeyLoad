using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

internal static class TestSuiteOutput
{
    private const int ModelAndDcpResourceStreamCount = 2;
    private const string NotificationsEndedBeforeCaptureStopped = "Aspire resource notifications ended before output capture was stopped.";
    private const string MissingSelectedResourceInstanceId = "Aspire did not provide the selected resource instance ID.";
    private const string TooManySelectedResourceIdentities = "Aspire reported more resource identities than the selected executable owns.";
    private const string CaptureDidNotSettleAfterCancellation = "Aspire output capture did not settle after owner cancellation.";

    internal static async Task ForwardAsync(DistributedApplication app, string resource, CancellationToken token)
    {
        var logs = app.Services.GetRequiredService<ResourceLoggerService>();
        using var captureLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var captures = new Dictionary<string, Task>(StringComparer.Ordinal);
        Exception? cleanupFailure = null;
        try
        {
            try
            {
                await CaptureResourcesAsync(app, resource, logs, captures, captureLifetime.Token).ConfigureAwait(false);
                throw new InvalidOperationException(NotificationsEndedBeforeCaptureStopped);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally
            {
                cleanupFailure = await CancelAndJoinAsync(captureLifetime, captures.Values).ConfigureAwait(false);
            }
        }
        catch (Exception primary)
        {
            if (cleanupFailure is not null)
            {
                throw new AggregateException(primary, cleanupFailure);
            }
            throw;
        }
        if (cleanupFailure is not null)
        {
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }
    }

    private static async Task CaptureResourcesAsync(DistributedApplication app, string resource,
        ResourceLoggerService logs, Dictionary<string, Task> captures, CancellationToken token)
    {
        if (app.ResourceNotifications.TryGetCurrentState(resource, out var current))
        {
            CaptureResource(logs, current.ResourceId, captures, token);
        }
        await foreach (var update in app.ResourceNotifications.WatchAsync(token).ConfigureAwait(false))
        {
            if (string.Equals(update.Resource.Name, resource, StringComparison.Ordinal))
            {
                CaptureResource(logs, update.ResourceId, captures, token);
            }
        }
    }

    private static void CaptureResource(ResourceLoggerService logs, string resourceId,
        Dictionary<string, Task> captures, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            throw new InvalidOperationException(MissingSelectedResourceInstanceId);
        }
        if (captures.ContainsKey(resourceId))
        {
            return;
        }
        if (captures.Count >= ModelAndDcpResourceStreamCount)
        {
            throw new InvalidOperationException(TooManySelectedResourceIdentities);
        }
        captures.Add(resourceId, ForwardResourceAsync(logs, resourceId, token));
    }

    private static async Task ForwardResourceAsync(ResourceLoggerService logs, string resourceId, CancellationToken token)
    {
        try
        {
            await foreach (var batch in logs.WatchAsync(resourceId).WithCancellation(token).ConfigureAwait(false))
            {
                await WriteAsync(batch).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private static async Task<Exception?> CancelAndJoinAsync(CancellationTokenSource lifetime, IEnumerable<Task> captures)
    {
        var cancellation = lifetime.CancelAsync();
        await cancellation.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        Exception? failure = cancellation.IsFaulted ? cancellation.Exception!.Flatten() : null;

        var join = Task.WhenAll(captures);
        await join.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (join.IsFaulted)
        {
            var errors = join.Exception!.Flatten().InnerExceptions;
            foreach (var item in errors)
            {
                failure = Combine(failure, item);
            }
        }
        else if (join.IsCanceled)
        {
            failure = Combine(failure, new OperationCanceledException(CaptureDidNotSettleAfterCancellation));
        }
        return failure;
    }

    private static Exception Combine(Exception? current, Exception addition)
        => current is null ? addition : new AggregateException(current, addition);

    private static async Task WriteAsync(IEnumerable<LogLine> lines)
    {
        foreach (var line in lines)
        {
            var writer = line.IsErrorMessage ? Console.Error : Console.Out;
            await writer.WriteLineAsync(line.Content).ConfigureAwait(false);
        }
    }
}
