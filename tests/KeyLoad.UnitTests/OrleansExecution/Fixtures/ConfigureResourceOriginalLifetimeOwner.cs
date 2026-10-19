using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.RecoveryTests.Features.ResourceExecution;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class ConfigureResourceOriginalLifetimeOwner
{
    internal static async Task RunAsync(Func<TestDatabase, RequestWorkOwner, Task> body, CancellationToken token)
    {
        using var fixture = new TestDatabase();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var owner = new RequestWorkOwner(UnitRoutingOptions.Routing());
            token.ThrowIfCancellationRequested();
            await ServerFailureObserver.ObserveAsync(() => body(fixture, owner), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task LeaseAsync(TestDatabase fixture, RequestWorkOwner owner, Guid requestId,
        Func<RequestWorkLease, ReadExecutionBudget, Task> body, CancellationToken token)
    {
        var failures = new List<Exception>();
        var cleanup = new List<Exception>();
        var constructed = false;
        ResourceLifetimeWorkResultRetention? ledger = null;
        RequestWorkLease? lease = null;
        var prepared = false;
        try
        {
            using var pending = new PendingResultRetention(fixture.Database.OperationLimitsOptions);
            constructed = true;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                lease = owner.Acquire(requestId, RequestWorkKind.CommandCapability);
                ledger = pending.TransferTo(lease);
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
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            if (!constructed)
            {
                throw;
            }
            cleanup.Add(error);
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            if (!constructed)
            {
                throw;
            }
            cleanup.Add(error);
        }
        finally
        {
            if (prepared)
            {
                ServerFailureObserver.Observe(() => ledger?.Dispose(), cleanup);
            }
        }
        await CompleteAsync(lease, prepared, failures, cleanup);
        failures.AddRange(cleanup);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(RequestWorkLease? lease, bool prepared,
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
