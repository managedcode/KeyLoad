using KeyLoad.Storage;

namespace KeyLoad.Server;

internal static class RuntimeJournalStorePreparation
{
    private const string CurrentCapabilityRequired = "The physical store must already require the current runtime journal reader.";

    internal static void Prepare(PartitionStores stores)
    {
        if (stores.Canonical.Identity.MinimumReaderContract != StoreReaderContract.RuntimeJournal)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, CurrentCapabilityRequired);
        }
    }

    internal static int CurrentCapabilityEvidence(PartitionHost partition)
    {
        if (!partition.CurrentCapabilityValidated)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, CurrentCapabilityRequired);
        }
        return StoreReaderContract.RuntimeJournal;
    }
}
