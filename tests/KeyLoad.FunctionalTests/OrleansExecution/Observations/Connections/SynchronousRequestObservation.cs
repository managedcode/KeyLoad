using System.Collections.Concurrent;
using KeyLoad.Clustering;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.FunctionalTests.OrleansExecution;

/// <summary>Holds a real operation on its executing thread, so an asynchronous yield cannot satisfy parallelism.</summary>
internal sealed class SynchronousRequestObservation(Guid heldRequest) : IDisposable
{
    private readonly ManualResetEventSlim release = new();
    private readonly ConcurrentDictionary<Guid, (GrainId Grain, ActivationId Activation, SiloAddress Silo)> executions = new();
    private int entered;
    internal TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal IGrainRequestPhaseObserver Wrap(ConnectionOperationObservation connection) => new Observer(this, connection);

    private void Observe(GrainRequestProbeIdentity identity, GrainRequestPhase phase, IGrainContext context,
        CancellationToken token)
    {
        if (phase != GrainRequestPhase.AuthorizationReload)
        {
            return;
        }
        executions[identity.RequestId] = (context.GrainId, context.ActivationId,
            context.ActivationServices.GetRequiredService<ILocalSiloDetails>().SiloAddress);
        if (identity.RequestId != heldRequest || Interlocked.Exchange(ref entered, 1) != 0)
        {
            return;
        }
        Arrived.TrySetResult();
        try
        {
            release.Wait(token);
        }
        finally
        {
            Exited.TrySetResult();
        }
    }

    internal async Task AssertSeparateExecutionAsync(Guid followingRequest)
    {
        var held = executions[heldRequest];
        var following = executions[followingRequest];
        await Assert.That(held.Silo).IsEqualTo(following.Silo);
        await Assert.That(held.Grain == following.Grain).IsFalse();
        await Assert.That(held.Activation == following.Activation).IsFalse();
    }

    internal void Release() => release.Set();
    public void Dispose() => release.Dispose();

    private sealed class Observer(SynchronousRequestObservation owner, ConnectionOperationObservation connection)
        : IGrainRequestPhaseObserver
    {
        public async ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
            IGrainContext context, CancellationToken cancellationToken)
        {
            owner.Observe(identity, phase, context, cancellationToken);
            await connection.ObserveAsync(identity, phase, context, cancellationToken).ConfigureAwait(true);
        }

        public void ProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext context)
            => connection.ProducerDisposed(identity, context);
    }
}
