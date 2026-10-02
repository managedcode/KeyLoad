using System.Security.Cryptography;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeBackupRestoreRestore
{
    internal static StoreIdentity Restore(string backup, string destination, Guid? newIncarnation,
        byte[]? newSigningKey)
    {
        EnsureDestinationIsEmpty(destination);
        var identity = CreateRestoredIdentity(ZoneTreeBackupRestoreFiles.ReadAndVerify(backup), newIncarnation,
            newSigningKey);
        ZoneTreeStoreFiles.CreatePrivateDirectory(destination);
        File.Copy(Path.Combine(backup, JournalFileName), Path.Combine(destination, JournalFileName));
        ZoneTreeIdentityFile.Write(Path.Combine(destination, IdentityFileName), identity);
        return ApplyRestoreAuthorityState(destination);
    }

    private static void EnsureDestinationIsEmpty(string destination)
    {
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        {
            throw Errors.Fail(ErrorCode.Conflict, RestoreDestinationNotEmpty);
        }
    }

    private static StoreIdentity CreateRestoredIdentity(StoreIdentity identity, Guid? newIncarnation,
        byte[]? newSigningKey)
    {
        return identity with
        {
            NodeId = Guid.NewGuid(),
            Incarnation = newIncarnation ?? Guid.NewGuid(),
            SigningKey = newSigningKey?.ToArray() ?? RandomNumberGenerator.GetBytes(SigningKeyBytes),
            DispatchPaused = true
        };
    }

    private static StoreIdentity ApplyRestoreAuthorityState(string destination)
    {
        using var restored = new ZoneTreeStore(new(destination));
        restored.Commit((tx, _) =>
        {
            tx.Delete(KeyCodec.Encode(SystemNamespace, LastAppliedKey));
            tx.Delete(KeyCodec.Encode(SystemNamespace, ClockKey));
            tx.Delete(KeyCodec.Encode(MembershipNamespace, OrleansMembershipKey));
            tx.PutRecord(KeyCodec.Encode(SystemNamespace, DispatchPausedKey), true);
            return true;
        });
        return restored.Identity;
    }
}
