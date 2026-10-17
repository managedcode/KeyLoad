using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query.Features.Search;
using KeyLoad.Replication;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.Server;

internal static class PartitionHostConstructionCleanup
{
    internal static async Task RunAsync(Exception error, NativeTextOnlineMaintenanceService? openedOnlineText,
        NativeTextIncrementalMaintenanceService? openedTextMaintenance, NativeAnnMaintenanceService? openedAnnMaintenance,
        ClusterCoordinator? openedCoordinator, ReplicaConsensus? openedConsensus, ReplicaMaterializer? applying,
        ITextProjection? openedText, DurableReplicaLog? openedLog, CacheMemoryBudget? openedCacheMemory,
        Features.Messaging.RemoteTransferPeerVerificationOwner? TransferVerification,
        Features.ClusterRouting.RequestCqrsCanonicalApplyBridge ApplyProbe, RowVisibilityCache? rowVisibility,
        PartitionStores? stores, Action requireTransferVerifierClosed)
    {
        const int FailuresCountValidationBoundary = 1;
        var failures = new List<Exception> { error };
        await DisposeDerivedOwnersAsync(openedOnlineText, openedTextMaintenance, openedAnnMaintenance,
            openedCoordinator, openedConsensus, failures).ConfigureAwait(false);
        Task? joiningMaterializer = null;
        if (applying is not null)
        {
            ServerFailureObserver.Observe(() => joiningMaterializer = applying.DisposeAsync().AsTask(), failures);
            if (joiningMaterializer is not null)
            {
                await ServerFailureObserver.ObserveAsync(() => joiningMaterializer, failures).ConfigureAwait(false);
            }
        }
        if (TransferVerification is not null && (applying is null || joiningMaterializer is { IsCompletedSuccessfully: true }))
        {
            ServerFailureObserver.Observe(TransferVerification.Dispose, failures);
        }
        ServerFailureObserver.Observe(ApplyProbe.CloseFrameObservation, failures);
        if (openedText is not null)
        {
            ServerFailureObserver.Observe(openedText.Dispose, failures);
        }
        if (openedLog is not null)
        {
            ServerFailureObserver.Observe(openedLog.Dispose, failures);
        }
        DisposeRowVisibility(rowVisibility, failures);
        if (openedCacheMemory is not null)
        {
            ServerFailureObserver.Observe(openedCacheMemory.Dispose, failures);
        }
        ServerFailureObserver.Observe(() =>
        {
            requireTransferVerifierClosed();
            stores?.Dispose();
        }, failures);
        if (failures.Count > FailuresCountValidationBoundary)
        {
            throw new AggregateException(failures);
        }
    }

    private static async Task DisposeDerivedOwnersAsync(NativeTextOnlineMaintenanceService? openedOnlineText,
        NativeTextIncrementalMaintenanceService? openedTextMaintenance, NativeAnnMaintenanceService? openedAnnMaintenance,
        ClusterCoordinator? openedCoordinator, ReplicaConsensus? openedConsensus, List<Exception> failures)
    {
        if (openedOnlineText is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => openedOnlineText.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        if (openedTextMaintenance is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => openedTextMaintenance.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        if (openedAnnMaintenance is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => openedAnnMaintenance.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        if (openedCoordinator is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => openedCoordinator.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        if (openedConsensus is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => openedConsensus.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
    }

    internal static void DisposeRowVisibility(RowVisibilityCache? rowVisibility, List<Exception> failures)
    {
        try
        {
            rowVisibility?.Dispose();
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        {
            failures.Add(error);
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            failures.Add(error);
        }
    }
}
