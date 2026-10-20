using KeyLoad.Clustering;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.RecoveryTests.Features.ResourceExecution;
using KeyLoad.Server;
using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreTestData;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsBoundCoreOwner
{
    internal static async Task RunAsync(Func<TestDatabase, RequestWorkOwner, CancellationToken, Task> body,
        CancellationToken token)
    {
        using var fixture = new TestDatabase();
        await AnalyticsBoundsOwner.SeedAsync(fixture, [D.Event(fixture, D.First, D.Alpha, 2) with { Links = [] }]);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var owner = new RequestWorkOwner(UnitRoutingOptions.Routing());
            await ServerFailureObserver.ObserveAsync(() => body(fixture, owner, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task LeaseAsync(TestDatabase fixture, RequestWorkOwner owner,
        Func<RequestWorkLease, ResourceLifetimeWorkResultRetention, ReadExecutionBudget, Task> body,
        CancellationToken token)
    {
        var failures = new List<Exception>();
        ResourceLifetimeWorkResultRetention? ledger = null;
        try
        {
            using var pending = new PendingResultRetention(fixture.Database.OperationLimitsOptions);
            try
            {
                var lease = owner.Acquire(Guid.NewGuid(), RequestWorkKind.ReadCapability);
                ServerFailureObserver.Observe(() =>
                {
                    ledger = pending.TransferTo(lease);
                }, failures);
                if (ledger is not null)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        var work = new ReadExecutionBudget(fixture.Database.OperationLimitsOptions, fixture.Database.EvaluationClock, token);
                        work.AttachResourceLifetimeBinding(lease);
                        await body(lease, ledger, work);
                    }, failures);
                }
                await FinishAsync(lease, ledger, failures);
            }
            catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task FinishAsync(RequestWorkLease lease,
        ResourceLifetimeWorkResultRetention? ledger, List<Exception> failures)
    {
        var primary = failures.FirstOrDefault();
        var cleanup = new List<Exception>();
        var prepared = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await lease.PrepareCompletionAsync();
            prepared = true;
        }, cleanup);
        if (prepared)
        {
            ServerFailureObserver.Observe(() => ledger?.Dispose(), cleanup);
            var cleanupFailure = cleanup.Count switch
            {
                0 => null,
                1 => cleanup[0],
                _ => new AggregateException(cleanup)
            };
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var settled = await lease.CompleteAsync(primary, cleanupFailure);
                if (settled.CleanupOrJoinFailure is { } failure && !cleanup.Contains(failure))
                {
                    throw failure;
                }
            }, cleanup);
        }
        failures.AddRange(cleanup);
    }
}
