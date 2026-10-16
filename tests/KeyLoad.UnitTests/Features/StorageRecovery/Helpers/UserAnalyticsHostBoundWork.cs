using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

/// <summary>Actual physical-host work ownership for native component reads; no quorum claim.</summary>
internal sealed class UserAnalyticsHostBoundWork(NativeRequestWorkOwner owner) : IAsyncDisposable
{
    private const int NoCleanupFailures = 0;
    private const int OneCleanupFailure = 1;
    private const int FirstCleanupFailure = 0;
    private const string MissingCatalog = "The native Analytics fixture requires its committed physical catalog.";

    internal static async Task<UserAnalyticsHostBoundWork> OpenAsync(PartitionHost host,
        IOptions<GrainRoutingOptions> routing, CancellationToken token)
    {
        NativeRequestWorkOwner? pending = new(routing);
        var failures = new List<Exception>();
        try
        {
            var catalog = host.Database.Store.Read(view => PhysicalShardCatalogRecordSerialization.Read(view))
                ?? throw new InvalidOperationException(MissingCatalog);
            var state = await host.Consensus.StateAsync(token);
            pending.BindPhysicalOwner(catalog.DefaultShard);
            pending.AttachResourceLifetimeWorkFence(host.Database, state.LastIndex);
            var result = new UserAnalyticsHostBoundWork(pending);
            pending = null;
            return result;
        }
        catch (Exception original)
        {
            failures.Add(original);
            throw;
        }
        finally
        {
            if (pending is not null)
            {
                try
                {
                    await pending.DisposeAsync();
                }
                catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
                {
                    failures.Add(error);
                }
                catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
                {
                    failures.Add(error);
                }
                ServerFailureObserver.ThrowIfAny(failures);
            }
        }
    }

    internal async Task AggregateAsync(PartitionHost host, UserAnalyticsRuntime runtime,
        AggregateAnalyticsRequest request, Func<AnalyticsAggregateResult, Task> consume,
        CancellationToken token, bool cancelBeforeNative = false)
    {
        var lease = owner.Acquire(Guid.NewGuid(), NativeRequestWorkKind.ReadCapability);
        ResourceLifetimeWorkResultRetention? pending = null;
        ResourceLifetimeWorkResultRetention? retention = null;
        Exception? original = null;
        var failures = new List<Exception>();
        try
        {
            pending = new ResourceLifetimeWorkResultRetention(host.Database.OperationLimitsOptions);
            lease.AttachResultRetention(pending);
            retention = pending;
            pending = null;
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
        finally
        {
            var cleanupFailures = new List<Exception>();
            try
            {
                pending?.Dispose();
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                cleanupFailures.Add(error);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                cleanupFailures.Add(error);
            }
            await FinishAsync(lease, retention, original, cleanupFailures, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
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
