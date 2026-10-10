using Aspire.Hosting.ApplicationModel;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class RemoteTransferStartupWindow : IAsyncDisposable
{
    private const string Completed = "Completed";
    private const string Cancelled = "Cancelled";
    private const string Faulted = "Faulted";
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, RemoteTransferStartupBuffer> buffers;
    private readonly Task[] logs;
    private readonly Task states;
    private bool disposed;

    internal RemoteTransferStartupWindow(ResourceLoggerService logger, ResourceNotificationService notifications,
        ContainerResource[] resources, Dictionary<string, RemoteTransferStartupBuffer> originalBuffers)
    {
        buffers = originalBuffers;
        logs = resources.Select(resource => CaptureAsync(logger, resource, buffers[resource.Name])).ToArray();
        states = StatesAsync(notifications);
    }

    internal IEnumerable<KeyValuePair<string, RemoteTransferStartupBuffer>> Buffers => buffers;

    private async Task CaptureAsync(ResourceLoggerService logger, ContainerResource resource,
        RemoteTransferStartupBuffer buffer)
    {
        try
        {
            await foreach (var batch in logger.WatchAsync(resource).WithCancellation(lifetime.Token).ConfigureAwait(false))
            { foreach (var line in batch) { buffer.Record(line.Content); } }
            buffer.Terminal(Completed);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { buffer.Terminal(Cancelled); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { buffer.Terminal(Faulted); throw; }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { buffer.Terminal(Faulted); throw; }
    }

    private async Task StatesAsync(ResourceNotificationService notifications)
    {
        try
        {
            await foreach (var value in notifications.WatchAsync(lifetime.Token).ConfigureAwait(false))
            {
                if (buffers.TryGetValue(value.Resource.Name, out var buffer))
                { buffer.State(value.Snapshot.State?.Text, value.Snapshot.ExitCode, value.Snapshot.HealthStatus?.ToString()); }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        { return; }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(lifetime.CancelAsync, failures).ConfigureAwait(false);
        foreach (var original in logs.Append(states))
        { await ServerFailureObserver.ObserveAsync(() => original, failures).ConfigureAwait(false); }
        try
        { lifetime.Dispose(); disposed = true; }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
