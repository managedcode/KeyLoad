using System.Runtime.ExceptionServices;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Owns and joins the actual native resource subscriptions for one C1 wave.</summary>
internal sealed class RequestCqrsRf3DiagnosticsCleanup : IAsyncDisposable
{
    private readonly System.Threading.Lock gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationTokenSource cleanupDeadline = new();
    private readonly ResourceLoggerService logger;
    private readonly ContainerResource[] resources;
    private readonly RequestCqrsRf3McpRejectionNodeCapture[] nodes;
    private readonly Guid waveId;
    private readonly Task[] subscriptions;
    private readonly Action<RequestCqrsLifecycleStage>? failureObserver;
    private Task? disposalTask;
    private bool fallbackRequested;

    internal RequestCqrsRf3DiagnosticsCleanup(Guid waveId, ResourceLoggerService logger,
        ContainerResource[] resources, RequestCqrsRf3McpRejectionNodeCapture[] nodes,
        Action<RequestCqrsLifecycleStage>? failureObserver = null)
    {
        this.waveId = waveId;
        this.logger = logger;
        this.resources = resources;
        this.nodes = nodes;
        this.failureObserver = failureObserver;
        subscriptions = resources.Select(resource => CaptureNodeAsync(logger, resource,
            nodes.Single(node => string.Equals(node.Name, resource.Name, StringComparison.Ordinal))))
            .ToArray();
    }

    internal bool IsJoined => subscriptions.All(subscription => subscription.IsCompleted);
    internal RequestCqrsCaptureLifecycleSnapshot ReadLifecycleSnapshot()
        => new(StatusFor(RequestCqrsRf3Protocol.Node1), StatusFor(RequestCqrsRf3Protocol.Node2),
            StatusFor(RequestCqrsRf3Protocol.Node3),
            lifetime.IsCancellationRequested, cleanupDeadline.IsCancellationRequested, fallbackRequested);

    private TaskStatus? StatusFor(string node)
    {
        var index = Array.FindIndex(resources, resource => string.Equals(resource.Name, node, StringComparison.Ordinal));
        return index < 0 ? null : subscriptions[index].Status;
    }

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
            await RequestCqrsLifecycleFailureObserver.ObserveAsync(() => drain.WaitAsync(cancellationToken),
                failures, failureObserver, RequestCqrsLifecycleStage.CaptureDrain).ConfigureAwait(false);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            RequestCqrsLifecycleFailureObserver.Append(failures, error, failureObserver,
            RequestCqrsLifecycleStage.CaptureDrain);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            RequestCqrsLifecycleFailureObserver.Append(failures, error, failureObserver,
            RequestCqrsLifecycleStage.CaptureDrain);
        }
        finally
        {
            if (!drain.IsCompleted)
            {
                fallbackRequested = true;
                await ObserveCleanupAsync(lifetime.CancelAsync, failures,
                    RequestCqrsLifecycleStage.CaptureFallbackCancellation).ConfigureAwait(false);
                await ObserveCleanupAsync(() => drain, failures,
                    RequestCqrsLifecycleStage.CaptureFallbackJoin).ConfigureAwait(false);
            }
            try
            { lifetime.Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                RequestCqrsLifecycleFailureObserver.Append(failures, error, failureObserver,
                RequestCqrsLifecycleStage.CaptureLifetimeDispose);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                RequestCqrsLifecycleFailureObserver.Append(failures, error, failureObserver,
                RequestCqrsLifecycleStage.CaptureLifetimeDispose);
            }
            try
            { cleanupDeadline.Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                RequestCqrsLifecycleFailureObserver.Append(failures, error, failureObserver,
                RequestCqrsLifecycleStage.CaptureDeadlineDispose);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                RequestCqrsLifecycleFailureObserver.Append(failures, error, failureObserver,
                RequestCqrsLifecycleStage.CaptureDeadlineDispose);
            }
        }
        ThrowWithNativeFatalPriority(failures);
    }

    private void CompleteResourceStreams(List<Exception> failures)
    {
        foreach (var resource in resources)
        {
            RequestCqrsLifecycleFailureObserver.Observe(() => logger.Complete(resource), failures,
                failureObserver, CompletionStage(resource.Name));
        }
    }

    private static RequestCqrsLifecycleStage CompletionStage(string resourceName) => resourceName switch
    {
        RequestCqrsRf3Protocol.Node1 => RequestCqrsLifecycleStage.CaptureCompleteNode1,
        RequestCqrsRf3Protocol.Node2 => RequestCqrsLifecycleStage.CaptureCompleteNode2,
        RequestCqrsRf3Protocol.Node3 => RequestCqrsLifecycleStage.CaptureCompleteNode3,
        _ => throw new ArgumentOutOfRangeException(nameof(resourceName))
    };

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

    private Task ObserveCleanupAsync(Func<Task> operation, List<Exception> failures,
        RequestCqrsLifecycleStage stage)
        => RequestCqrsLifecycleFailureObserver.ObserveAsync(operation, failures, failureObserver, stage);

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
