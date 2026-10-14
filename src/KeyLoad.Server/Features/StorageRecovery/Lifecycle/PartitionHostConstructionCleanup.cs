using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query.Features.Search;
using KeyLoad.Replication;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.Server;

internal static class PartitionHostConstructionCleanup
{
    internal static void Run(Exception error, NativeTextOnlineMaintenanceService? openedOnlineText,
        NativeTextIncrementalMaintenanceService? openedTextMaintenance, NativeAnnMaintenanceService? openedAnnMaintenance,
        ClusterCoordinator? openedCoordinator, ReplicaConsensus? openedConsensus, ReplicaMaterializer? applying,
        ITextProjection? openedText, DurableReplicaLog? openedLog, CacheMemoryBudget? openedCacheMemory,
        Features.Messaging.RemoteTransferPeerVerificationOwner? TransferVerification,
        Features.ClusterRouting.RequestCqrsCanonicalApplyBridge ApplyProbe, RowVisibilityCache? rowVisibility,
        PartitionStores stores, Action requireTransferVerifierClosed)
    {
        const int FailuresCountValidationBoundary = 1;
        var failures = new List<Exception> { error };
        DisposeDerivedOwners(openedOnlineText, openedTextMaintenance, openedAnnMaintenance,
            openedCoordinator, openedConsensus, failures);
        Task? joiningMaterializer = null;
        if (applying is not null)
        {
            joiningMaterializer = applying.DisposeAsync().AsTask();
            ServerFailureObserver.Observe(() => joiningMaterializer.GetAwaiter().GetResult(), failures);
        }
        if (TransferVerification is not null && (joiningMaterializer is null || joiningMaterializer.IsCompletedSuccessfully))
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
            stores.Dispose();
        }, failures);
        if (failures.Count > FailuresCountValidationBoundary)
        {
            throw new AggregateException(failures);
        }
    }

    private static void DisposeDerivedOwners(NativeTextOnlineMaintenanceService? openedOnlineText,
        NativeTextIncrementalMaintenanceService? openedTextMaintenance, NativeAnnMaintenanceService? openedAnnMaintenance,
        ClusterCoordinator? openedCoordinator, ReplicaConsensus? openedConsensus, List<Exception> failures)
    {
        if (openedOnlineText is not null)
        {
            ServerFailureObserver.Observe(() => openedOnlineText.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures);
        }
        if (openedTextMaintenance is not null)
        {
            ServerFailureObserver.Observe(() => openedTextMaintenance.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures);
        }
        if (openedAnnMaintenance is not null)
        {
            ServerFailureObserver.Observe(() => openedAnnMaintenance.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures);
        }
        if (openedCoordinator is not null)
        {
            ServerFailureObserver.Observe(() => openedCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures);
        }
        if (openedConsensus is not null)
        {
            ServerFailureObserver.Observe(() => openedConsensus.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures);
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
