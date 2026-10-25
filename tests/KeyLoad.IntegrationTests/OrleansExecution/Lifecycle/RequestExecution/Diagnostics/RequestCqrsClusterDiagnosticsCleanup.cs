using System.Runtime.ExceptionServices;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.OrleansExecution.Helpers;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.OrleansExecution;

/// <summary>Owns and joins the actual execution resource subscriptions for one C1 wave.</summary>
internal sealed class RequestCqrsClusterDiagnosticsCleanup : IAsyncDisposable
{
    private readonly System.Threading.Lock gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource cleanupDeadline = new();
    private readonly ResourceLoggerService logger;
    private readonly ProjectResource[] resources;
    private readonly RequestCqrsClusterMcpRejectionNodeCapture[] nodes;
    private readonly Guid waveId;
    private readonly Task[] subscriptions;
    private readonly Action<RequestCqrsLifecycleStage>? failureObserver;
    private readonly RequestCqrsClusterSubscriberTransitionHooks? subscriberTransitions;
    private readonly RequestCqrsResourceCompletionEvidence completionEvidence = new();
    private Task? disposalTask;
    private bool forcedCancellationRequested;

    internal RequestCqrsClusterDiagnosticsCleanup(Guid waveId, ResourceLoggerService logger,
        ProjectResource[] resources, RequestCqrsClusterMcpRejectionNodeCapture[] nodes,
        Action<RequestCqrsLifecycleStage>? failureObserver = null,
        RequestCqrsClusterSubscriberTransitionHooks? subscriberTransitions = null)
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
        => new((StatusFor(RequestCqrsClusterProtocol.Node1)).Value, (StatusFor(RequestCqrsClusterProtocol.Node2)).Value,
            (StatusFor(RequestCqrsClusterProtocol.Node3)).Value,
            lifetime.IsCancellationRequested, cleanupDeadline.IsCancellationRequested, forcedCancellationRequested,
            completionEvidence.Snapshot());

    private global::ManagedCode.Communication.Result<TaskStatus?> StatusFor(string node)
    {
        var index = Array.FindIndex(resources, resource => string.Equals(resource.Name, node, StringComparison.Ordinal));
        return index < 0 ? global::ManagedCode.Communication.Result<TaskStatus?>.FailNotFound(nameof(StatusFor)) : global::ManagedCode.Communication.Result<TaskStatus?>.Succeed(((TaskStatus?)(subscriptions[index].Status)));
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
                cleanupDeadline = new CancellationTokenSource(RequestCqrsClusterProtocol.CleanupDeadline, TimeProvider.System);
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
            RequestCqrsClusterSubscriberTransitionHooks.Observe(() => subscriberTransitions?.Begin(), failures, failureObserver,
                RequestCqrsLifecycleStage.CaptureDrain);
            RequestCqrsClusterResourceStreamCompletion.Complete(completionEvidence, logger, resources,
                (outcomeArgument0) => (StatusFor(outcomeArgument0)).Value, failures, failureObserver);
            RequestCqrsClusterSubscriberTransitionHooks.Observe(() => subscriberTransitions?.Drain(false), failures, failureObserver,
                RequestCqrsLifecycleStage.CaptureDrain);
            RequestCqrsLifecycleFailureObserver.Observe(() => completionEvidence.StartDrain(
                cancellationToken.IsCancellationRequested, (StatusFor(RequestCqrsClusterProtocol.Node1)).Value,
                (StatusFor(RequestCqrsClusterProtocol.Node2)).Value, (StatusFor(RequestCqrsClusterProtocol.Node3)).Value),
                failures, failureObserver, RequestCqrsLifecycleStage.CaptureDrain);
            var failuresBeforeDrain = failures.Count;
            await RequestCqrsLifecycleFailureObserver.ObserveAsync(async () =>
            {
                await drain.WaitAsync(cancellationToken).ConfigureAwait(false);
                completionEvidence.ReturnDrain();
            }, failures, stage => completionEvidence.ObserveDrainFailure(stage, (outcomeArgument0) => (StatusFor(outcomeArgument0)).Value,
                failureObserver, cancellationToken),
                RequestCqrsLifecycleStage.CaptureDrain).ConfigureAwait(false);
            if (failures.Count > failuresBeforeDrain)
            {
                RequestCqrsClusterSubscriberTransitionHooks.Observe(() => subscriberTransitions?.Drain(false), failures, failureObserver,
                    RequestCqrsLifecycleStage.CaptureDrain);
                RequestCqrsClusterSubscriberTransitionHooks.Observe(() => subscriberTransitions?.WriteDiagnostic(subscriptions.Count(subscription =>
                    subscription.Status == TaskStatus.RanToCompletion)), failures, failureObserver,
                    RequestCqrsLifecycleStage.CaptureDrain);
            }
        }
        finally
        {
            await CancelAndJoinDrainAsync(drain, failures).ConfigureAwait(false);
            RequestCqrsClusterSubscriberTransitionHooks.Observe(() => subscriberTransitions?.Drain(true), failures, failureObserver,
                forcedCancellationRequested ? RequestCqrsLifecycleStage.CaptureForcedJoin : RequestCqrsLifecycleStage.CaptureDrain);
            if (forcedCancellationRequested)
            {
                RequestCqrsClusterSubscriberTransitionHooks.Observe(() => subscriberTransitions?.WriteDiagnostic(subscriptions.Count(subscription =>
                    subscription.Status == TaskStatus.RanToCompletion)), failures, failureObserver,
                    RequestCqrsLifecycleStage.CaptureForcedJoin);
            }
            var lifetimeDisposal = DisposeLifetimeAsync();
            await ObserveCleanupAsync(() => lifetimeDisposal, failures,
                RequestCqrsLifecycleStage.CaptureLifetimeDispose).ConfigureAwait(false);
            var deadlineDisposal = DisposeDeadlineAsync();
            await ObserveCleanupAsync(() => deadlineDisposal, failures,
                RequestCqrsLifecycleStage.CaptureDeadlineDispose).ConfigureAwait(false);
        }
        RuntimeThrowWithFatalPriority(failures);
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

    private async Task CancelAndJoinDrainAsync(Task drain, List<Exception> failures)
    {
        if (drain.IsCompleted)
        {
            completionEvidence.RecordOriginalJoin(lifetime.IsCancellationRequested, (outcomeArgument0) => (StatusFor(outcomeArgument0)).Value, failures,
                failureObserver, RequestCqrsLifecycleStage.CaptureDrain);
            return;
        }
        forcedCancellationRequested = true;
        RequestCqrsLifecycleFailureObserver.Observe(() => completionEvidence.EnterForcedCancellation(
            lifetime.IsCancellationRequested), failures, failureObserver,
            RequestCqrsLifecycleStage.CaptureForcedCancellation);
        await ObserveCleanupAsync(lifetime.CancelAsync, failures,
            RequestCqrsLifecycleStage.CaptureForcedCancellation).ConfigureAwait(false);
        await ObserveCleanupAsync(() => drain, failures,
            RequestCqrsLifecycleStage.CaptureForcedJoin).ConfigureAwait(false);
        completionEvidence.RecordOriginalJoin(lifetime.IsCancellationRequested, (outcomeArgument0) => (StatusFor(outcomeArgument0)).Value, failures,
            failureObserver, RequestCqrsLifecycleStage.CaptureForcedJoin);
    }

    private async Task CaptureNodeAsync(ResourceLoggerService resourceLogger, ProjectResource resource,
        RequestCqrsClusterMcpRejectionNodeCapture node)
    {
        try
        {
            await foreach (var batch in resourceLogger.WatchAsync(resource)
                .WithCancellation(lifetime.Token).ConfigureAwait(false))
            {
                foreach (var line in batch)
                {
                    node.TryCapture(line.Content, waveId);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            node.Complete();
        }
    }

    private Task ObserveCleanupAsync(Func<Task> operation, List<Exception> failures,
        RequestCqrsLifecycleStage stage)
        => RequestCqrsLifecycleFailureObserver.ObserveAsync(operation, failures, failureObserver, stage);

    private static void RuntimeThrowWithFatalPriority(List<Exception> failures)
    {
        var fatalIndex = failures.FindIndex(error => !CqrsBoundaryErrors.IsNonFatal(error));
        if (fatalIndex < 0)
        {
            ServerFailureObserver.ThrowIfAny(failures);
            return;
        }
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        var prioritized = new List<Exception>(failures.Count) { failures[fatalIndex] };
        for (var index = 0; index < failures.Count; index++)
        {
            if (index != fatalIndex)
            {
                prioritized.Add(failures[index]);
            }
        }
        ServerFailureObserver.ThrowIfAny(prioritized);
    }
}
