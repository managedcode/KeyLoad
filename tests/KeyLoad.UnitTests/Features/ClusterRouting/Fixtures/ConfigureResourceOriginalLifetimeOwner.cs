using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ConfigureResourceOriginalLifetimeOwner
{
    internal static async Task RunAsync(Func<TestDatabase, NativeRequestWorkOwner, Task> body, CancellationToken token)
    {
        using var fixture = new TestDatabase(nativeReplicaAdmission: true);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var owner = new NativeRequestWorkOwner(UnitRoutingOptions.Routing());
            var physical = fixture.Store.Read(view => PhysicalShardCatalogRecordSerialization.Read(view)?.DefaultShard)
                ?? throw new InvalidOperationException("The original native physical catalog is absent.");
            owner.BindPhysicalOwner(physical);
            owner.AttachResourceLifetimeWorkFence(fixture.Database, fixture.ReadJournalCut().LastIndex);
            token.ThrowIfCancellationRequested();
            await ServerFailureObserver.ObserveAsync(() => body(fixture, owner), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task LeaseAsync(TestDatabase fixture, NativeRequestWorkOwner owner, Guid requestId,
        Func<NativeRequestWorkLease, ReadExecutionBudget, Task> body, CancellationToken token)
    {
        var failures = new List<Exception>();
        var cleanup = new List<Exception>();
        ResourceLifetimeWorkResultRetention? ledger = null;
        NativeRequestWorkLease? lease = null;
        var prepared = false;
        try
        {
            ledger = new ResourceLifetimeWorkResultRetention(fixture.Database.OperationLimitsOptions);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                lease = owner.Acquire(requestId, NativeRequestWorkKind.CommandCapability);
                lease.AttachResultRetention(ledger);
                var work = fixture.CreateReadWork(token);
                work.AttachResourceLifetimeBinding(lease);
                await body(lease, work);
            }, failures);
            if (lease is not null)
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await lease.PrepareCompletionAsync();
                    prepared = true;
                }, cleanup);
            }
        }
        finally
        {
            if (lease is null || prepared)
            {
                try
                {
                    ledger?.Dispose();
                }
                catch (Exception disposalFailure) when (NativeCqrsBoundaryErrors.IsNonFatal(disposalFailure))
                {
                    cleanup.Add(disposalFailure);
                }
                catch (Exception disposalFailure) when (!NativeCqrsBoundaryErrors.IsNonFatal(disposalFailure))
                {
                    cleanup.Add(disposalFailure);
                }
            }
        }
        await CompleteAsync(lease, prepared, failures, cleanup);
        failures.AddRange(cleanup);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(NativeRequestWorkLease? lease, bool prepared,
        List<Exception> failures, List<Exception> cleanup)
    {
        if (lease is null || !prepared)
        {
            return;
        }
        var combined = cleanup.Count switch
        {
            0 => null,
            1 => cleanup[0],
            _ => new AggregateException(cleanup)
        };
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var settled = await lease.CompleteAsync(failures.FirstOrDefault(), combined);
            if (settled.CleanupOrJoinFailure is { } failure && !cleanup.Contains(failure))
            {
                throw failure;
            }
        }, cleanup);
    }
}
