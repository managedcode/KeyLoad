using Aspire.Hosting.ApplicationModel;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

/// <summary>Owns one exact native instance admission and its original subscriber reader.</summary>
internal sealed class RequestCqrsRf3NativeNodeAdmission(ContainerResource resource) : IAsyncDisposable
{
    private const string ChangedInstanceMessage = "The owned C1 resource changed its admitted native instance.";
    private readonly System.Threading.Lock gate = new();
    private readonly RequestCqrsRf3DiagnosticsSubscriberObserver observer = new();
    private string? instanceId;

    internal ContainerResource Resource { get; } = resource;
    internal bool IsAdmitted => OriginalCallback is { IsCompletedSuccessfully: true } && observer.IsJoined;
    internal Task? OriginalCallback { get; private set; }

    internal Task StartAsync(ResourceLoggerService logger, string actualId, Task modelProof,
        Action startCapture, RequestCqrsLifecycleEvidence? lifecycle,
        Action<RequestCqrsLifecycleStage>? failureObserver, CancellationToken ownerToken, CancellationToken nativeToken)
    {
        lock (gate)
        {
            if (instanceId is not null && !string.Equals(instanceId, actualId, StringComparison.Ordinal))
            { throw new InvalidOperationException(ChangedInstanceMessage); }
            instanceId = actualId;
            return OriginalCallback ??= RunAsync(logger, actualId, modelProof, startCapture, lifecycle,
                failureObserver, ownerToken, nativeToken);
        }
    }

    private async Task RunAsync(ResourceLoggerService logger, string actualId, Task modelProof,
        Action startCapture, RequestCqrsLifecycleEvidence? lifecycle,
        Action<RequestCqrsLifecycleStage>? failureObserver, CancellationToken ownerToken, CancellationToken nativeToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ownerToken, nativeToken);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            lifecycle?.SetStage(RequestCqrsLifecycleStage.SubscriberAdmission);
            observer.Initialize(logger, linked.Token, failureObserver);
            lifecycle?.BindObserver(observer);
            startCapture();
            await observer.WaitForResourceAsync(actualId, Resource.Name).ConfigureAwait(false);
            await modelProof.WaitAsync(linked.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        lifecycle?.RecordFirstFailureIfAny(failures);
        await RequestCqrsLifecycleFailureObserver.ObserveAsync(() => DisposeAsync().AsTask(), failures,
            failureObserver, RequestCqrsLifecycleStage.ObserverJoin).ConfigureAwait(false);
        if (!observer.IsJoined)
        { await observer.RetryFailedCloseAsync(failures, failureObserver).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public ValueTask DisposeAsync() => observer.DisposeAsync();

    internal Task RetryFailedCloseAsync(List<Exception> failures, Action<RequestCqrsLifecycleStage>? failureObserver)
        => observer.RetryFailedCloseAsync(failures, failureObserver);
}
