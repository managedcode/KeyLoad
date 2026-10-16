using KeyLoad.Clustering;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterReplication;
using KeyLoad.UnitTests.Features.ClusterRouting;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeResourceCommandBindingFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var scenario = new P();
        var queues = new NativeReplicaCommandLeaseQueues();
        var failures = new List<Exception>();
        NativeRequestWorkLease? lease = null;
        ResourceLifetimeWorkResultRetention? ledger = null;
        CancellationTokenSource? bound = null;
        CancellationTokenSource? linked = null;
        CancellationTokenSource? execution = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new RequestCqrsCohortRuntimeFixture(barrierDeadline: scenario);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                bound = new(RequestCqrsCohortRuntimeFixture.StartupBound, TimeProvider.System);
                linked = CancellationTokenSource.CreateLinkedTokenSource(token, bound.Token);
                var seeded = await NativeLocalChildLifetimeSeed.CreateAsync(fixture, linked.Token);
                await queues.StartAsync(fixture, linked.Token);
                var leader = seeded.Leader;
                var queue = queues.Queues[leader.Journal.Configuration.LocalId];
                var owner = queues.Work[leader.Journal.Configuration.LocalId];
                ledger = new(leader.Journal.Database.OperationLimitsOptions);
                lease = owner.Acquire(Guid.NewGuid(), NativeRequestWorkKind.CommandCapability);
                lease.AttachResultRetention(ledger);
                execution = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, lease.Cancellation);
                var committed = await NativeResourceCommandBindingChecks.CommandsAsync(fixture, seeded, queue,
                    lease, ledger, execution.Token);
                await NativeResourceCommandBindingChecks.ReadsAsync(fixture, seeded, lease, ledger, execution.Token);
                await NativeResourceCommandBindingChecks.ReplayAsync(fixture, seeded, queue, committed.Operation,
                    committed.Receipt, execution.Token);
            }, failures);
            scenario.Release();
            await FinishAsync(lease, ledger, failures);
            ServerFailureObserver.Observe(() => execution?.Dispose(), failures);
            await queues.CloseAsync(failures);
        }, failures);
        ServerFailureObserver.Observe(() => linked?.Dispose(), failures);
        ServerFailureObserver.Observe(() => bound?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task FinishAsync(NativeRequestWorkLease? lease,
        ResourceLifetimeWorkResultRetention? ledger, List<Exception> failures)
    {
        var original = failures.FirstOrDefault();
        var cleanup = new List<Exception>();
        var prepared = lease is null;
        if (lease is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await lease.PrepareCompletionAsync();
                prepared = true;
            }, cleanup);
        }
        if (prepared)
        {
            ServerFailureObserver.Observe(() => ledger?.Dispose(), cleanup);
        }
        if (lease is not null && prepared)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var outcome = await lease.CompleteAsync(original,
                    cleanup.Count == NativeResourceCommandBindingChecks.NoEntries ? null : new AggregateException(cleanup));
                if (outcome.CleanupOrJoinFailure is { } failure)
                {
                    throw failure;
                }
            }, cleanup);
        }
        failures.AddRange(cleanup);
    }
}
