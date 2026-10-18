using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.UnitTests.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsHostedNodeResources : IAsyncDisposable
{
    private readonly Lock gate = new();
    private Task? disposal;
    private readonly ClusterCoordinator? coordinator;
    internal NativeRequestWorkOwner Work
    {
        get;
    }
    internal GrainRequestCodec Codec
    {
        get;
    }
    internal PhysicalShardRecord PhysicalOwner
    {
        get;
    }
    internal NativeTextMaintenanceTestRuntime? Text
    {
        get;
    }
    internal NativeAnnMaintenanceTestRuntime? Ann
    {
        get;
    }
    internal bool IsJoined => Work.IsJoined;
    internal bool CanCloseStorage => Work.IsJoined && disposal?.IsCompletedSuccessfully == true;
    internal ClusterCoordinator Coordinator => coordinator
        ?? throw new InvalidOperationException(RequestCqrsHostedProtocol.CoordinatorAbsent);

    internal RequestCqrsHostedNodeResources(RequestCqrsHostedProfile profile, ReplicaLifecycleFixture journal)
    {
        Work = new(profile.Routing);
        var database = journal.Database;
        try
        {
            PhysicalOwner = database.Store.Read(view => PhysicalShardCatalogRecordSerialization.Read(view)?.DefaultShard)
                ?? throw new InvalidOperationException(RequestCqrsHostedProtocol.CatalogAbsent);
            Codec = new(database, profile.Clock, profile.Routing)
            {
                PhaseObserver = profile.Observer,
                PurposeObserver = profile.PurposeObserver
            };
            Work.BindPhysicalOwner(PhysicalOwner);
            Work.AttachResourceLifetimeWorkFence(database, journal.Log.State.LastIndex);
            coordinator = new(journal.Consensus, database, new(Options.Create(new CommandAdmissionLimits())),
                profile.Clock, journal.ExecutionOptions, Options.Create(new CommandInboxExecutionOptions()));
            NativeReplicaCommandExecution.StartAsync(coordinator, Work, database,
                profile.Observer as IReplicaForwardedCommandObservation, CancellationToken.None).GetAwaiter().GetResult();
            Text = profile.NativeText ? new(database, journal.CanonicalDirectoryPath, profile.Clock) : null;
            Ann = profile.NativeAnn ? new(database, journal.CanonicalDirectoryPath, profile.Clock) : null;
        }
        catch (Exception primary)
        {
            try
            {
                DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(primary, cleanup);
            }
            throw;
        }
    }

    internal async Task JoinRequestsAsync()
    {
        var failures = new List<Exception>();
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(Work.DrainAsync, failures);
        if (!Work.IsJoined)
        {
            KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            throw new InvalidOperationException(RequestCqrsHostedProtocol.WorkNotJoined);
        }
        if (coordinator is not null)
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                () => coordinator.StopAsync(CancellationToken.None), failures);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            return new(disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(JoinRequestsAsync, failures);
        if (!Work.IsJoined)
        {
            KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            throw new InvalidOperationException(RequestCqrsHostedProtocol.WorkNotJoined);
        }
        if (Text is not null)
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => Text.DisposeAsync().AsTask(), failures);
        }
        if (Ann is not null)
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => Ann.DisposeAsync().AsTask(), failures);
        }
        if (coordinator is not null)
        {
            try
            {
                await coordinator.DisposeAsync();
            }
            catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                failures.Add(cleanup);
            }
            catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            {
                failures.Add(cleanup);
            }
        }
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => Work.DisposeAsync().AsTask(), failures);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
