using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.CrashHost.Features.ResourceExecution;
using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.CrashHost.Features.AnalyticsStore;

/// <summary>Actual physical-host work ownership for native component reads; no quorum claim.</summary>
internal sealed class UserAnalyticsProcessBoundWork : IAsyncDisposable
{
    private readonly NativeRequestWorkOwner owner;

    private UserAnalyticsProcessBoundWork(IOptions<GrainRoutingOptions> routing)
        => owner = new NativeRequestWorkOwner(routing);

    internal static async Task<UserAnalyticsProcessBoundWork> OpenAsync(PartitionHost host,
        IOptions<GrainRoutingOptions> routing, CancellationToken token)
    {
        var work = new UserAnalyticsProcessBoundWork(routing);
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
        ResourceLifetimeWorkResultRetention? attached = null;
        Exception? original = null;
        Exception? cleanup = null;
        var failures = new List<Exception>();
        var acquired = false;
        try
        {
            using var pending = new PendingResultRetention(host.Database.OperationLimitsOptions);
            acquired = true;
            try
            {
                attached = pending.TransferTo(lease);
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
                cleanup = error;
            }
            else
            {
                original = error;
            }
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            if (acquired)
            {
                cleanup = error;
            }
            else
            {
                original = error;
            }
            failures.Add(error);
        }
        var preparation = await PrepareAsync(lease, failures);
        if (preparation is null)
        {
            cleanup = DisposeAttachedRetention(attached, cleanup, failures);
            await SettleAsync(lease, original, cleanup, failures);
        }
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

    private static Exception? DisposeAttachedRetention(ResourceLifetimeWorkResultRetention? attached,
        Exception? cleanup, List<Exception> failures)
    {
        try
        {
            attached?.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            cleanup = NativeRequestWorkSettlement.Preserve(cleanup, error);
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            cleanup = NativeRequestWorkSettlement.Preserve(cleanup, error);
            failures.Add(error);
        }
        return cleanup;
    }

    private static async Task<Exception?> PrepareAsync(NativeRequestWorkLease lease, List<Exception> failures)
    {
        try
        {
            await lease.PrepareCompletionAsync();
            return null;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
            return error;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
            return error;
        }
    }

    private static Task SettleAsync(NativeRequestWorkLease lease, Exception? original, Exception? cleanup,
        List<Exception> failures)
        => ServerFailureObserver.ObserveAsync(async () =>
        {
            lease.PrepareDisposal(original, cleanup);
            await lease.DisposeAsync();
            var settled = await lease.Settlement;
            if (settled.CleanupOrJoinFailure is { } failure && !ReferenceEquals(failure, cleanup))
            {
                throw failure;
            }
        }, failures);

    internal bool IsJoined => owner.IsJoined;

    public ValueTask DisposeAsync() => owner.DisposeAsync();
}
