using System.Runtime.ExceptionServices;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Owns and joins the actual native resource subscriptions for one C1 wave.</summary>
internal sealed class RequestCqrsRf3DiagnosticsCleanup : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationTokenSource cleanupDeadline = new();
    private readonly ResourceLoggerService logger;
    private readonly ContainerResource[] resources;
    private readonly RequestCqrsRf3McpRejectionNodeCapture[] nodes;
    private readonly Guid waveId;
    private readonly Task[] subscriptions;
    private Task? disposalTask;

    internal RequestCqrsRf3DiagnosticsCleanup(Guid waveId, ResourceLoggerService logger,
        ContainerResource[] resources, RequestCqrsRf3McpRejectionNodeCapture[] nodes)
    {
        this.waveId = waveId;
        this.logger = logger;
        this.resources = resources;
        this.nodes = nodes;
        subscriptions = resources.Select(resource => CaptureNodeAsync(logger, resource,
            nodes.Single(node => string.Equals(node.Name, resource.Name, StringComparison.Ordinal))))
            .ToArray();
    }

    internal bool IsJoined => subscriptions.All(subscription => subscription.IsCompleted);

    internal Task CompleteAndDrainAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            disposalTask ??= DisposeCoreAsync(cancellationToken);
            return disposalTask;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposalTask is null)
            {
                cleanupDeadline.CancelAfter(RequestCqrsRf3Protocol.CleanupDeadline);
                disposalTask = DisposeCoreAsync(cleanupDeadline.Token);
            }
            return new(disposalTask);
        }
    }

    private async Task DisposeCoreAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        var drain = Task.WhenAll(subscriptions);
        try
        {
            CompleteResourceStreams(failures);
            await ServerFailureObserver.ObserveAsync(() => drain.WaitAsync(cancellationToken), failures)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        finally
        {
            if (!drain.IsCompleted)
            {
                await ObserveCleanupAsync(lifetime.CancelAsync, failures).ConfigureAwait(false);
                await ObserveCleanupAsync(() => drain, failures).ConfigureAwait(false);
            }
            try
            { lifetime.Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            { failures.Add(error); }
            try
            { cleanupDeadline.Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            { failures.Add(error); }
        }
        ThrowWithNativeFatalPriority(failures);
    }

    private void CompleteResourceStreams(List<Exception> failures)
    {
        foreach (var resource in resources)
        {
            try
            { ServerFailureObserver.Observe(() => logger.Complete(resource), failures); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            { failures.Add(error); }
        }
    }

    private async Task CaptureNodeAsync(ResourceLoggerService resourceLogger, ContainerResource resource,
        RequestCqrsRf3McpRejectionNodeCapture node)
    {
        try
        {
            await foreach (var batch in resourceLogger.WatchAsync(resource)
                .WithCancellation(lifetime.Token).ConfigureAwait(false))
            {
                foreach (var line in batch)
                { node.TryCapture(line.Content, waveId); }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        { node.Complete(); }
    }

    private static async Task ObserveCleanupAsync(Func<Task> operation, List<Exception> failures)
    {
        try
        { await ServerFailureObserver.ObserveAsync(operation, failures).ConfigureAwait(false); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
    }

    private static void ThrowWithNativeFatalPriority(List<Exception> failures)
    {
        var fatalIndex = failures.FindIndex(error => !NativeCqrsBoundaryErrors.IsNonFatal(error));
        if (fatalIndex < 0)
        { ServerFailureObserver.ThrowIfAny(failures); return; }
        if (failures.Count == 1)
        { ExceptionDispatchInfo.Capture(failures[0]).Throw(); }
        var prioritized = new List<Exception>(failures.Count) { failures[fatalIndex] };
        for (var index = 0; index < failures.Count; index++)
        {
            if (index != fatalIndex)
            { prioritized.Add(failures[index]); }
        }
        ServerFailureObserver.ThrowIfAny(prioritized);
    }
}
