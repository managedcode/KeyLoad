using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.CrashHost.Features.ResourceExecution;
using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

/// <summary>Actual physical-host work ownership for bounded native component reads.</summary>
internal sealed class UserAnalyticsHostBoundWork : IAsyncDisposable
{
    private const int NoCleanupFailures = 0;
    private const int OneCleanupFailure = 1;
    private const int FirstCleanupFailure = 0;

    private readonly NativeRequestWorkOwner owner;

    private UserAnalyticsHostBoundWork(IOptions<GrainRoutingOptions> routing)
        => owner = new NativeRequestWorkOwner(routing);

    internal static async Task<UserAnalyticsHostBoundWork> OpenAsync(PartitionHost host,
        IOptions<GrainRoutingOptions> routing, CancellationToken token)
    {
        var work = new UserAnalyticsHostBoundWork(routing);
        try
        {
            token.ThrowIfCancellationRequested();
            var identity = host.Database.Store.Identity;
            work.owner.BindPhysicalOwner(new(identity.NodeId, identity.Incarnation));
            return work;
        }
        catch (Exception original) when (NativeCqrsBoundaryErrors.IsNonFatal(original))
        {
            var failures = new List<Exception> { original };
            try
            {
                await work.DisposeAsync();
            }
            catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                failures.Add(cleanup);
            }
            catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                failures.Add(cleanup);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
        catch (Exception original) when (!NativeCqrsBoundaryErrors.IsNonFatal(original))
        {
            var failures = new List<Exception> { original };
            try
            {
                await work.DisposeAsync();
            }
            catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                failures.Add(cleanup);
            }
            catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                failures.Add(cleanup);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal async Task AggregateAsync(PartitionHost host, UserAnalyticsRuntime runtime,
        AggregateAnalyticsRequest request, Func<AnalyticsAggregateResult, Task> consume,
        CancellationToken token, bool cancelBeforeNative = false)
    {
        var lease = owner.Acquire(Guid.NewGuid(), NativeRequestWorkKind.ReadCapability);
        ResourceLifetimeWorkResultRetention? retention = null;
        Exception? original = null;
        var failures = new List<Exception>();
        var cleanupFailures = new List<Exception>();
        var acquired = false;
        try
        {
            using var pending = new PendingResultRetention(host.Database.OperationLimitsOptions);
            acquired = true;
            try
            {
                retention = pending.TransferTo(lease);
                await ExecuteNativeAsync(host, runtime, request, consume, lease, cancelBeforeNative, token);
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                original = error;
                failures.Add(error);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                original = error;
                failures.Add(error);
            }
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            if (acquired)
            {
                cleanupFailures.Add(error);
            }
            else
            {
                original = error;
                failures.Add(error);
            }
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            if (acquired)
            {
                cleanupFailures.Add(error);
            }
            else
            {
                original = error;
                failures.Add(error);
            }
        }
        await FinishAsync(lease, retention, original, cleanupFailures, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task ExecuteNativeAsync(PartitionHost host, UserAnalyticsRuntime runtime,
        AggregateAnalyticsRequest request, Func<AnalyticsAggregateResult, Task> consume,
        NativeRequestWorkLease lease, bool cancelBeforeNative, CancellationToken token)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, owner.ShutdownToken, lease.Cancellation);
        var work = new ReadExecutionBudget(host.Database.OperationLimitsOptions,
            host.Database.EvaluationClock, cancellation.Token);
        using var admission = host.Database.AdmitQuery(cancellation.Token);
        var snapshot = host.Database.CaptureAnalyticsAggregateBound(PartitionStoreProtocol.AdministratorId,
            request, work, lease);
        if (cancelBeforeNative)
        {
            await cancellation.CancelAsync();
        }

        using var result = await runtime.AggregateAsync(snapshot, lease, cancellation.Token);
        host.Database.ValidateAnalyticsAggregatePublication(snapshot);
        work.CheckResult(result.Result);
        await consume(result.Result);
    }

    private static async Task FinishAsync(NativeRequestWorkLease lease,
        ResourceLifetimeWorkResultRetention? retention, Exception? original,
        List<Exception> cleanupFailures, List<Exception> failures)
    {
        var prepared = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await lease.PrepareCompletionAsync();
            prepared = true;
        }, cleanupFailures);
        if (!prepared)
        {
            failures.AddRange(cleanupFailures);
            return;
        }
        ServerFailureObserver.Observe(() => retention?.Dispose(), cleanupFailures);
        await SettleAsync(lease, original, cleanupFailures, failures);
    }

    private static async Task SettleAsync(NativeRequestWorkLease lease, Exception? original,
        List<Exception> cleanupFailures, List<Exception> failures)
    {
        var cleanup = cleanupFailures.Count switch
        {
            NoCleanupFailures => null,
            OneCleanupFailure => cleanupFailures[FirstCleanupFailure],
            _ => new AggregateException(cleanupFailures)
        };
        failures.AddRange(cleanupFailures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var settled = await lease.CompleteAsync(original, cleanup);
            if (settled.CleanupOrJoinFailure is { } failure && !ReferenceEquals(failure, cleanup))
            {
                throw failure;
            }
        }, failures);
    }

    internal bool IsJoined => owner.IsJoined;

    public ValueTask DisposeAsync() => owner.DisposeAsync();
}
