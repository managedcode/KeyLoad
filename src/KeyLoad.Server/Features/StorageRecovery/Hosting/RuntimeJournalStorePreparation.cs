using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class RuntimeJournalStorePreparation
{
    private const string BackupRequired = "An existing unmarked store requires KeyLoad:RuntimeJournal:ReaderUpgradeBackupDirectory before native journal adoption.";
    private const string BackupMismatch = "The verified reader-upgrade backup does not match the current physical store cut.";
    private const string CanonicalBackup = "canonical";
    private const string ReplicaBackup = "replica";

    internal static void Prepare(PartitionStores stores, IOptions<RuntimeJournalOptions> options,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions)
    {
        var settings = options.Value;
        var backup = settings.ReaderUpgradeBackupDirectory;
        if (NeedsBackup(stores.Canonical) || NeedsBackup(stores.Replica))
        {
            if (string.IsNullOrWhiteSpace(backup))
            {
                throw Errors.Fail(ErrorCode.FormatUnsupported, BackupRequired);
            }
            var directory = Path.Combine(backup, stores.Canonical.Identity.NodeId.ToString("N"));
            VerifyBackup(stores.Canonical, Path.Combine(directory, CanonicalBackup), executionOptions);
            VerifyBackup(stores.Replica, Path.Combine(directory, ReplicaBackup), executionOptions);
        }
        stores.Canonical.RequireReaderContract(StoreReaderContract.RuntimeJournal);
        stores.Replica.RequireReaderContract(StoreReaderContract.RuntimeJournal);
    }

    internal static int ReaderEvidence(PartitionHost partition)
    {
        if (partition.Database.Store.Identity.MinimumReaderContract != StoreReaderContract.RuntimeJournal
            || !partition.RuntimeJournalStoresMarked)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, BackupMismatch);
        }
        return StoreReaderContract.RuntimeJournal;
    }

    private static bool NeedsBackup(ZoneTreeStore store)
        => store.Identity.MinimumReaderContract == StoreReaderContract.Legacy && store.Position > 0;

    private static void VerifyBackup(ZoneTreeStore store, string directory,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions)
    {
        if (!Directory.Exists(directory))
        {
            store.CreateBackup(directory);
        }
        var verified = ZoneTreeStore.VerifyBackup(directory, executionOptions);
        var identity = store.Identity;
        if (verified.Position != store.Position || verified.Identity.NodeId != identity.NodeId
            || verified.Identity.Incarnation != identity.Incarnation
            || !CryptographicOperations.FixedTimeEquals(verified.Identity.SigningKey.Span, identity.SigningKey.Span))
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, BackupMismatch);
        }
    }
}
