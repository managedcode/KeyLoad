using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.Server;

internal static class PartitionHostConstructionCleanup
{
    internal static async Task RunAsync(Exception error, NativeTextOnlineMaintenanceService? openedOnlineText,
        NativeTextIncrementalMaintenanceService? openedTextMaintenance, NativeAnnMaintenanceService? openedAnnMaintenance,
        ITextProjection? openedText, CacheMemoryBudget? openedCacheMemory,
        Features.Messaging.RemoteTransferPeerVerificationOwner? TransferVerification, RowVisibilityCache? rowVisibility,
        PartitionStores? stores, Action requireTransferVerifierClosed)
    {
        const int FailuresCountValidationBoundary = 1;
        var failures = new List<Exception> { error };
        await DisposeDerivedOwnersAsync(openedOnlineText, openedTextMaintenance, openedAnnMaintenance,
            failures).ConfigureAwait(false);
        if (TransferVerification is not null)
        {
            ServerFailureObserver.Observe(TransferVerification.Dispose, failures);
        }
        if (openedText is not null)
        {
            ServerFailureObserver.Observe(openedText.Dispose, failures);
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
        List<Exception> failures)
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
