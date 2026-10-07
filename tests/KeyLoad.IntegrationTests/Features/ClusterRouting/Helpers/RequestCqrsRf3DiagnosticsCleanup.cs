using System.Runtime.ExceptionServices;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Owns and joins the actual native resource subscriptions for one C1 wave.</summary>
internal sealed class RequestCqrsRf3DiagnosticsCleanup : IAsyncDisposable
{
    private readonly System.Threading.Lock gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource cleanupDeadline = new();
    private readonly ResourceLoggerService logger;
    private readonly ContainerResource[] resources;
    private readonly RequestCqrsRf3McpRejectionNodeCapture[] nodes;
    private readonly Guid waveId;
    private readonly Task[] subscriptions;
    private readonly Action<RequestCqrsLifecycleStage>? failureObserver;
    private readonly RequestCqrsRf3SubscriberTransitionHooks? subscriberTransitions;
    private readonly RequestCqrsResourceCompletionEvidence completionEvidence = new();
    private Task? disposalTask;
    private bool fallbackRequested;

    internal RequestCqrsRf3DiagnosticsCleanup(Guid waveId, ResourceLoggerService logger,
        ContainerResource[] resources, RequestCqrsRf3McpRejectionNodeCapture[] nodes,
        Action<RequestCqrsLifecycleStage>? failureObserver = null,
        RequestCqrsRf3SubscriberTransitionHooks? subscriberTransitions = null)
    {
        this.waveId = waveId;
        this.logger = logger;
        this.resources = resources;
        this.nodes = nodes;
        this.failureObserver = failureObserver;
        this.subscriberTransitions = subscriberTransitions;
        subscriptions = resources.Select(resource => CaptureNodeAsync(logger, resource,
            nodes.Single(node => string.Equals(node.Name, resource.Name, StringComparison.Ordinal))))
            .ToArray();
    }

    internal bool IsJoined => subscriptions.All(subscription => subscription.IsCompleted);
    internal RequestCqrsCaptureLifecycleSnapshot ReadLifecycleSnapshot()
        => new(StatusFor(RequestCqrsRf3Protocol.Node1), StatusFor(RequestCqrsRf3Protocol.Node2),
            StatusFor(RequestCqrsRf3Protocol.Node3),
            lifetime.IsCancellationRequested, cleanupDeadline.IsCancellationRequested, fallbackRequested,
            completionEvidence.Snapshot());

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
                cleanupDeadline.Dispose();
                cleanupDeadline = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline, TimeProvider.System);
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
            RequestCqrsRf3SubscriberTransitionHooks.Observe(() => subscriberTransitions?.Begin(), failures, failureObserver,
                RequestCqrsLifecycleStage.CaptureDrain);
            RequestCqrsRf3ResourceStreamCompletion.Complete(completionEvidence, logger, resources,
                StatusFor, failures, failureObserver);
            RequestCqrsRf3SubscriberTransitionHooks.Observe(() => subscriberTransitions?.Drain(false), failures, failureObserver,
                RequestCqrsLifecycleStage.CaptureDrain);
            RequestCqrsLifecycleFailureObserver.Observe(() => completionEvidence.StartDrain(
                cancellationToken.IsCancellationRequested, StatusFor(RequestCqrsRf3Protocol.Node1),
                StatusFor(RequestCqrsRf3Protocol.Node2), StatusFor(RequestCqrsRf3Protocol.Node3)),
                failures, failureObserver, RequestCqrsLifecycleStage.CaptureDrain);
            var failuresBeforeDrain = failures.Count;
            await RequestCqrsLifecycleFailureObserver.ObserveAsync(async () =>
            {
                await drain.WaitAsync(cancellationToken).ConfigureAwait(false);
                completionEvidence.ReturnDrain();
            }, failures, stage => completionEvidence.ObserveDrainFailure(stage, StatusFor,
                failureObserver, cancellationToken),
                RequestCqrsLifecycleStage.CaptureDrain).ConfigureAwait(false);
            if (failures.Count > failuresBeforeDrain)
            {
                RequestCqrsRf3SubscriberTransitionHooks.Observe(() => subscriberTransitions?.Drain(false), failures, failureObserver,
                    RequestCqrsLifecycleStage.CaptureDrain);
                RequestCqrsRf3SubscriberTransitionHooks.Observe(() => subscriberTransitions?.WriteDiagnostic(subscriptions.Count(subscription =>
                    subscription.Status == TaskStatus.RanToCompletion)), failures, failureObserver,
                    RequestCqrsLifecycleStage.CaptureDrain);
            }
        }
        finally
        {
            await JoinFallbackAsync(drain, failures).ConfigureAwait(false);
            RequestCqrsRf3SubscriberTransitionHooks.Observe(() => subscriberTransitions?.Drain(true), failures, failureObserver,
                fallbackRequested ? RequestCqrsLifecycleStage.CaptureFallbackJoin : RequestCqrsLifecycleStage.CaptureDrain);
            if (fallbackRequested)
            {
                RequestCqrsRf3SubscriberTransitionHooks.Observe(() => subscriberTransitions?.WriteDiagnostic(subscriptions.Count(subscription =>
                    subscription.Status == TaskStatus.RanToCompletion)), failures, failureObserver,
                    RequestCqrsLifecycleStage.CaptureFallbackJoin);
            }
            var lifetimeDisposal = DisposeLifetimeAsync();
            await ObserveCleanupAsync(() => lifetimeDisposal, failures,
                RequestCqrsLifecycleStage.CaptureLifetimeDispose).ConfigureAwait(false);
            var deadlineDisposal = DisposeDeadlineAsync();
            await ObserveCleanupAsync(() => deadlineDisposal, failures,
                RequestCqrsLifecycleStage.CaptureDeadlineDispose).ConfigureAwait(false);
        }
        ThrowWithNativeFatalPriority(failures);
    }

    private async Task DisposeLifetimeAsync()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        lifetime.Dispose();
    }

    private async Task DisposeDeadlineAsync()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        cleanupDeadline.Dispose();
    }

    private async Task JoinFallbackAsync(Task drain, List<Exception> failures)
    {
        if (drain.IsCompleted)
        {
            completionEvidence.RecordOriginalJoin(lifetime.IsCancellationRequested, StatusFor, failures,
                failureObserver, RequestCqrsLifecycleStage.CaptureDrain);
            return;
        }
        fallbackRequested = true;
        RequestCqrsLifecycleFailureObserver.Observe(() => completionEvidence.EnterFallback(
            lifetime.IsCancellationRequested), failures, failureObserver,
            RequestCqrsLifecycleStage.CaptureFallbackCancellation);
        await ObserveCleanupAsync(lifetime.CancelAsync, failures,
            RequestCqrsLifecycleStage.CaptureFallbackCancellation).ConfigureAwait(false);
        await ObserveCleanupAsync(() => drain, failures,
            RequestCqrsLifecycleStage.CaptureFallbackJoin).ConfigureAwait(false);
        completionEvidence.RecordOriginalJoin(lifetime.IsCancellationRequested, StatusFor, failures,
            failureObserver, RequestCqrsLifecycleStage.CaptureFallbackJoin);
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
