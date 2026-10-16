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
    private const string MissingCatalog = "The native Analytics fixture requires its committed physical catalog.";

    internal static async Task<UserAnalyticsHostBoundWork> OpenAsync(PartitionHost host,
        IOptions<GrainRoutingOptions> routing, CancellationToken token)
    {
        var owner = new NativeRequestWorkOwner(routing);
        try
        {
            var catalog = host.Database.Store.Read(view => PhysicalShardCatalogRecordSerialization.Read(view))
                ?? throw new InvalidOperationException(MissingCatalog);
            var state = await host.Consensus.StateAsync(token);
            owner.BindPhysicalOwner(catalog.DefaultShard);
            owner.AttachResourceLifetimeWorkFence(host.Database, state.LastIndex);
            return new(owner);
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            await ServerFailureObserver.ObserveAsync(() => owner.DisposeAsync().AsTask(), failures);
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
        try
        {
            retention = new ResourceLifetimeWorkResultRetention(host.Database.OperationLimitsOptions);
            lease.AttachResultRetention(retention);
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
        catch (Exception error)
        {
            original = error;
            failures.Add(error);
        }
        Exception? cleanup = null;
        try
        {
            retention?.Dispose();
        }
        catch (Exception error) { cleanup = error; failures.Add(error); }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var settled = await lease.CompleteAsync(original, cleanup);
            if (settled.CleanupOrJoinFailure is { } failure && !ReferenceEquals(failure, cleanup))
            {
                throw failure;
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal bool IsJoined => owner.IsJoined;

    public ValueTask DisposeAsync() => owner.DisposeAsync();
}
