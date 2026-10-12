using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class ReplicaTransportObservedLifecycle(ILogger<SiloLifecycleSubject> logger) : ISiloLifecycle
{
    private readonly SiloLifecycleSubject subject = new(logger);
    internal Task? OriginalStop { get; private set; }
    internal TaskCompletionSource<Task> StopObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int HighestCompletedStage => subject.HighestCompletedStage;
    public int LowestStoppedStage => subject.LowestStoppedStage;
    internal Task StartAsync(CancellationToken token) => subject.OnStart(token);
    internal Task StopAsync(CancellationToken token) => subject.OnStop(token);

    public IDisposable Subscribe(string name, int stage, ILifecycleObserver observer)
        => subject.Subscribe(name, stage, new Observed(this, observer));

    private sealed class Observed(ReplicaTransportObservedLifecycle owner, ILifecycleObserver observer) : ILifecycleObserver
    {
        public Task OnStart(CancellationToken token) => observer.OnStart(token);
        public Task OnStop(CancellationToken token)
        {
            var original = observer.OnStop(token);
            owner.OriginalStop = original;
            owner.StopObserved.TrySetResult(original);
            return original;
        }
    }
}
