using Aspire.Hosting.ApplicationModel;
using KeyLoad.Orleans;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

/// <summary>Admits only actual owned RF3 instances before their native resource-start callbacks return.</summary>
internal sealed class RequestCqrsRf3NativeStartupAdmission : IAsyncDisposable
{
    private const string InvalidResourceMessage = "The native C1 start did not identify exactly its owned resource instance.";
    private readonly System.Threading.Lock gate = new();
    private readonly ContainerResource[] resources;
    private readonly RequestCqrsRf3NativeNodeAdmission[] nodes;
    private readonly CancellationTokenSource lifetime;
    private readonly CancellationToken lifetimeToken;
    private readonly TaskCompletionSource modelProof = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Guid waveId;
    private readonly RequestCqrsLifecycleEvidence? lifecycle;
    private readonly Action<RequestCqrsLifecycleStage>? failureObserver;
    private Task? disposal;

    internal RequestCqrsRf3NativeStartupAdmission(ContainerResource[] resources, Guid waveId,
        RequestCqrsLifecycleEvidence? lifecycle, Action<RequestCqrsLifecycleStage>? failureObserver,
        CancellationToken cancellationToken)
    {
        this.resources = resources;
        nodes = resources.Select(resource => new RequestCqrsRf3NativeNodeAdmission(resource)).ToArray();
        this.waveId = waveId;
        this.lifecycle = lifecycle;
        this.failureObserver = failureObserver;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetimeToken = lifetime.Token;
    }

    internal int AdmittedNodeCount => nodes.Count(node => node.IsAdmitted);
    internal RequestCqrsRf3Diagnostics? Diagnostics { get; private set; }
    internal bool IsJoined => disposal is { IsCompletedSuccessfully: true }
        && nodes.All(node => node.OriginalCallback is null or { IsCompleted: true });

    internal Task BeforeResourceStartsAsync(BeforeResourceStartedEvent actual, CancellationToken nativeToken)
    {
        var node = nodes.SingleOrDefault(candidate => ReferenceEquals(candidate.Resource, actual.Resource));
        if (node is null)
        { return Task.CompletedTask; }
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposal is not null, this);
            lifetimeToken.ThrowIfCancellationRequested();
            var notifications = actual.Services.GetRequiredService<ResourceNotificationService>();
            if (!notifications.TryGetCurrentState(node.Resource.Name, out var state)
                || !ReferenceEquals(state.Resource, node.Resource) || string.IsNullOrWhiteSpace(state.ResourceId))
            { throw new InvalidOperationException(InvalidResourceMessage); }
            var logger = actual.Services.GetRequiredService<ResourceLoggerService>();
            return node.StartAsync(logger, state.ResourceId, modelProof.Task,
                () => StartCapture(logger), lifecycle, failureObserver, lifetimeToken, nativeToken);
        }
    }

    private void StartCapture(ResourceLoggerService logger)
    {
        lock (gate)
        {
            if (Diagnostics is not null)
            { return; }
            Diagnostics = RequestCqrsRf3Diagnostics.Start(waveId, resources, logger, failureObserver);
            lifecycle?.BindDiagnostics(Diagnostics);
        }
    }

    internal void ReleaseVerifiedModel() => modelProof.TrySetResult();

    public ValueTask DisposeAsync()
    {
        lock (gate)
        { return new(disposal ??= DisposeCoreAsync()); }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        await RequestCqrsLifecycleFailureObserver.ObserveAsync(lifetime.CancelAsync, failures,
            failureObserver, RequestCqrsLifecycleStage.ObserverCancellation).ConfigureAwait(false);
        foreach (var node in nodes)
        {
            if (node.OriginalCallback is { } callback)
            {
                await RequestCqrsLifecycleFailureObserver.ObserveAsync(() => callback, failures,
                    failureObserver, RequestCqrsLifecycleStage.ObserverJoin).ConfigureAwait(false);
            }
            await RequestCqrsLifecycleFailureObserver.ObserveAsync(() => node.DisposeAsync().AsTask(), failures,
                failureObserver, RequestCqrsLifecycleStage.ObserverJoin).ConfigureAwait(false);
            await node.RetryFailedCloseAsync(failures, failureObserver).ConfigureAwait(false);
        }
        try
        { lifetime.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            RequestCqrsLifecycleFailureObserver.Append(failures, error, failureObserver,
                RequestCqrsLifecycleStage.ObserverLifetimeDispose);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            RequestCqrsLifecycleFailureObserver.Append(failures, error, failureObserver,
                RequestCqrsLifecycleStage.ObserverLifetimeDispose);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
