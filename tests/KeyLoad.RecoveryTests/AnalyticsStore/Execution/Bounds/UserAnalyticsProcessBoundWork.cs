using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.RecoveryTests.ResourceExecution;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.AnalyticsStore;

/// <summary>Actual physical-host work ownership for execution component reads; no quorum claim.</summary>
internal sealed class UserAnalyticsProcessBoundWork : IAsyncDisposable
{
    private readonly RequestWorkOwner owner;

    private UserAnalyticsProcessBoundWork()
        => owner = new RequestWorkOwner();

    internal static async Task<UserAnalyticsProcessBoundWork> OpenAsync(CancellationToken token)
    {
        var work = new UserAnalyticsProcessBoundWork();
        try
        {
            token.ThrowIfCancellationRequested();
            return work;
        }
        catch (Exception original) when (CqrsBoundaryErrors.IsNonFatal(original))
        {
            var failures = new List<Exception> { original };
            try
            {
                await work.DisposeAsync();
            }
            catch (Exception cleanup) when (CqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                failures.Add(cleanup);
            }
            catch (Exception cleanup) when (!CqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                failures.Add(cleanup);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
        catch (Exception original) when (!CqrsBoundaryErrors.IsNonFatal(original))
        {
            var failures = new List<Exception> { original };
            try
            {
                await work.DisposeAsync();
            }
            catch (Exception cleanup) when (CqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                failures.Add(cleanup);
            }
            catch (Exception cleanup) when (!CqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                failures.Add(cleanup);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal async Task AggregateAsync(PartitionHost host, UserAnalyticsRuntime runtime,
        AggregateAnalyticsRequest request, Func<AnalyticsAggregateResult, Task> consume,
        CancellationToken token, bool cancelBefore = false)
    {
        var lease = owner.Acquire(Guid.NewGuid(), RequestWorkKind.ReadCapability);
        ResourceLifetimeWorkResultRetention? attached = null;
        Exception? original = null;
        Exception? cleanup = null;
        var failures = new List<Exception>();
        var acquired = false;
        try
        {
            using var pending = new PendingResultRetention();
            acquired = true;
            try
            {
                attached = pending.TransferTo(lease);
                await RuntimeExecuteAsync(host, runtime, request, consume, lease, cancelBefore, token);
            }
            catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
            {
                original = error;
                failures.Add(error);
            }
            catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
            {
                original = error;
                failures.Add(error);
            }
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
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
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
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
        var preparation = (await PrepareAsync(lease, failures)).Value;
        if (preparation is null)
        {
            cleanup = DisposeAttachedRetention(attached, cleanup, failures);
            await SettleAsync(lease, original, cleanup, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task RuntimeExecuteAsync(PartitionHost host, UserAnalyticsRuntime runtime,
        AggregateAnalyticsRequest request, Func<AnalyticsAggregateResult, Task> consume,
        RequestWorkLease lease, bool cancelBefore, CancellationToken token)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, owner.ShutdownToken, lease.Cancellation);
        var work = new ReadExecutionBudget(host.Database.EvaluationClock, cancellation.Token);
        using var admission = host.Database.AdmitQuery(cancellation.Token);
        var snapshot = host.Database.CaptureAnalyticsAggregateBound(PartitionStoreProtocol.AdministratorId,
            request, work, lease);
        if (cancelBefore)
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
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            cleanup = RequestWorkSettlement.Preserve(cleanup, error);
            failures.Add(error);
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            cleanup = RequestWorkSettlement.Preserve(cleanup, error);
            failures.Add(error);
        }
        return cleanup;
    }

    private static async Task<global::ManagedCode.Communication.Result<Exception?>> PrepareAsync(RequestWorkLease lease, List<Exception> failures)
    {
        try
        {
            await lease.PrepareCompletionAsync();
            return global::ManagedCode.Communication.Result<Exception?>.FailNotFound(nameof(PrepareAsync));
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
            return global::ManagedCode.Communication.Result<Exception?>.Succeed(((Exception?)(error)));
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
            return global::ManagedCode.Communication.Result<Exception?>.Succeed(((Exception?)(error)));
        }
    }

    private static Task SettleAsync(RequestWorkLease lease, Exception? original, Exception? cleanup,
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
