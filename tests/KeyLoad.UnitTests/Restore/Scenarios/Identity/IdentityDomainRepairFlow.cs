using System.Security.Cryptography;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.BackupRestore;

internal static class IdentityDomainRepairFlow
{
    private const string RootPrefix = "keyload-identity-authority-";
    private const string RootKey = "KeyLoad.IdentityAuthorityRetainedRoot";
    private const string OwnerKey = "KeyLoad.IdentityAuthorityUnsettledOwner";
    private const string Source = "source";
    private const string Backup = "backup";
    private const string Restore = "restore";
    private const string Staging = ".keyload-restore-*";
    private const int NoFailures = 0;
    private const int MaximumRows = 128;
    private const string IncompleteRows = "The identity authority fixture exceeded its complete-row bound.";
    private const string GuidFormat = "N";

    internal static async Task RunAsync(bool negativeGeneration, CancellationToken token)
    {
        var root = Path.Combine(AppContext.BaseDirectory, RootPrefix + Guid.NewGuid().ToString(GuidFormat));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => CompleteAsync(root, negativeGeneration, token), failures);
        if (failures.Count == NoFailures)
        {
            ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures);
        }
        foreach (var failure in failures)
        {
            failure.Data[RootKey] = root;
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(string root, bool negativeGeneration, CancellationToken token)
    {
        var source = Path.Combine(root, Source);
        var backup = Path.Combine(root, Backup);
        var target = Path.Combine(root, Restore);
        await OwnAsync(source, store =>
        {
            store.Commit((transaction, _) =>
            {
                transaction.PutRecord(MetadataBackupFixture.StoredKeyBytes, MetadataBackupFixture.ExpectedValue);
                return true;
            });
            store.CreateBackup(backup);
            return Task.CompletedTask;
        }, token);
        var original = await ReadAsync(source, token);
        var archive = await BackupJournalRepairAssertions.ArchiveAsync(backup, token);
        var identityPath = Path.Combine(source, ZoneTreePersistenceFormat.IdentityFileName);
        var originalIdentity = await File.ReadAllBytesAsync(identityPath, token);
        var changed = negativeGeneration ? original.Identity with
        {
            ReadGeneration = -1L
        }
            : original.Identity with
            {
                Durability = (DurabilityProfile)int.MaxValue
            };
        var payload = OrleansSerialization.Serialize(changed);
        var fault = ZoneTreeMetadataBinary.Write(new ZoneTreeIdentityEnvelope(payload, SHA256.HashData(payload)),
            ZoneTreeMetadataBinary.IdentityMagic);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await File.WriteAllBytesAsync(identityPath, fault, token);
            await File.WriteAllBytesAsync(Path.Combine(backup, ZoneTreePersistenceFormat.IdentityFileName), fault, token);
            await MetadataTestFiles.UpdateManifestFileAsync(backup, ZoneTreePersistenceFormat.IdentityFileName);
            await RefuseAsync(root, source, backup, target, token);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(identityPath,
            originalIdentity, CancellationToken.None), failures);
        await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(
            Path.Combine(backup, ZoneTreePersistenceFormat.IdentityFileName),
            archive[ZoneTreePersistenceFormat.IdentityFileName], CancellationToken.None), failures);
        await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(
            Path.Combine(backup, ZoneTreePersistenceFormat.BackupManifestFileName),
            archive[ZoneTreePersistenceFormat.BackupManifestFileName], CancellationToken.None), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await BackupJournalRepairAssertions.SameArchiveAsync(archive, backup, token);
        await BackupJournalRepairAssertions.SameCutAsync(original,
            await ReadAsync(source, token));
        await IdentityDomainRepairCold.RunAsync(root, source, backup, target, original, token);
        await BackupJournalRepairAssertions.SameArchiveAsync(archive, backup, token);
    }

    private static async Task RefuseAsync(string root, string source, string backup, string target,
        CancellationToken token)
    {
        var files = await BackupJournalRepairAssertions.ArchiveAsync(source, token);
        var archive = await BackupJournalRepairAssertions.ArchiveAsync(backup, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RequireAsync(() => ZoneTreeStore.ValidateIdentityBeforeOpen(
            new(source), UnitExecutionOptions.StorageExecution())), failures);
        await ServerFailureObserver.ObserveAsync(() => RefuseOpenAsync(source), failures);
        await ServerFailureObserver.ObserveAsync(() => RequireAsync(() => ZoneTreeStore.VerifyBackup(
            backup, UnitExecutionOptions.StorageExecution())), failures);
        await ServerFailureObserver.ObserveAsync(() => RequireAsync(() => ZoneTreeStore.Restore(
            backup, target, UnitExecutionOptions.StorageExecution())), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(Directory.Exists(target)).IsFalse(), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(Directory.EnumerateDirectories(root,
            Staging, SearchOption.AllDirectories).Any()).IsFalse(), failures);
        await ServerFailureObserver.ObserveAsync(() => BackupJournalRepairAssertions.SameArchiveAsync(files, source, token), failures);
        await ServerFailureObserver.ObserveAsync(() => BackupJournalRepairAssertions.SameArchiveAsync(archive, backup, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RefuseOpenAsync(string source)
    {
        ZoneTreeStore? opened = null;
        var opening = new List<Exception>();
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => opened = new ZoneTreeStore(new(source),
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()), opening);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(opened).IsNull();
            await Assert.That(opening.Count).IsEqualTo(1);
            await Assert.That(opening[0].GetType()).IsEqualTo(typeof(KeyLoadException));
            var error = (KeyLoadException)opening[0];
            await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(error.Message).IsEqualTo(ZoneTreePersistenceFormat.IdentityChecksumInvalid);
        }, failures);
        if (opened is not null)
        {
            var closing = new List<Exception>();
            ServerFailureObserver.Observe(opened.Dispose, closing);
            foreach (var error in closing)
            {
                error.Data[OwnerKey] = opened;
                failures.Add(error);
            }
        }
        if (failures.Count != NoFailures)
        {
            failures.AddRange(opening);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<(StoreIdentity Identity, long Position, string[] Rows)> ReadAsync(
        string directory, CancellationToken token)
    {
        (StoreIdentity Identity, long Position, string[] Rows) result = default;
        await OwnAsync(directory, store =>
        {
            result = store.Read(view =>
            {
                var page = view.Scan([], MaximumRows);
                if (page.HasMore)
                {
                    throw new InvalidOperationException(IncompleteRows);
                }
                return (store.Identity, store.Position, page.Records.Select(row =>
                    Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray());
            });
            return Task.CompletedTask;
        }, token);
        return result;
    }

    private static async Task RequireAsync(Action operation)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo(ZoneTreePersistenceFormat.IdentityChecksumInvalid);
    }

    internal static async Task OwnAsync(string directory, Func<ZoneTreeStore, Task> body, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ZoneTreeStore? store = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            store = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(),
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(() => body(store), failures);
        }, failures);
        if (store is not null)
        {
            var closing = new List<Exception>();
            ServerFailureObserver.Observe(store.Dispose, closing);
            foreach (var error in closing)
            {
                error.Data[OwnerKey] = store;
                failures.Add(error);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
