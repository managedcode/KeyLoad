using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

/// <summary>Keeps the original metadata fixture quarantined after an uncertain inspection or store close.</summary>
internal sealed class MetadataEntryOpenOwner
{
    private const string Unsettled = "The original metadata inspection or native store is not confirmed settled.";
    private readonly StorageFailureOwnerLifetime stores = new();
    private bool IsInspectionUncertain
    {
        get; set;
    }
    internal ZoneTreeExistingStoreFixture Files { get; } = new();
    internal bool CanAdvance => !IsInspectionUncertain && stores.CanAdvance;

    internal async Task<ExistingStoreInspectorExit> InspectAsync()
    {
        if (!CanAdvance)
        {
            throw new InvalidOperationException(Unsettled);
        }
        try
        {
            var result = await Files.InspectAsync().ConfigureAwait(false);
            IsInspectionUncertain = !result.ProcessReaped || !result.StdoutReaderSettled
                || !result.StderrReaderSettled || !result.OuterOwnerReleased;
            return result;
        }
        catch (Exception)
        {
            IsInspectionUncertain = true;
            throw;
        }
    }

    internal ZoneTreeStore? OpenStore(List<Exception> failures)
    {
        if (!CanAdvance)
        {
            throw new InvalidOperationException(Unsettled);
        }
        return stores.Acquire(() => new ZoneTreeStore(Files.Options,
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()), failures);
    }

    internal void CloseStore(ZoneTreeStore store, List<Exception> failures) => stores.Close(store, failures);

    internal void Cleanup(List<Exception> failures)
    {
        if (CanAdvance)
        {
            ServerFailureObserver.Observe(Files.Dispose, failures);
        }
    }
}
