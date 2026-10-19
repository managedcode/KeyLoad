using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class JournalOriginalFailureAssertions
{
    internal static async Task FailAsync(ZoneTreeStoreRuntime runtime, ZoneTreeStore store,
        ZoneTreeExistingStoreFixture files)
    {
        await JournalOriginalFailureCut.RowsAsync(store, files, healthy: false);
        var identity = OrleansSerialization.Serialize(store.Identity);
        var identityFile = await File.ReadAllBytesAsync(files.IdentityPath);
        var position = store.Position;
        var originalJournal = runtime.Journal;
        await originalJournal.DisposeAsync();
        await Assert.That(ReferenceEquals(runtime.Journal, originalJournal)).IsTrue();
        await Assert.That(originalJournal.CanWrite).IsFalse();
        var journal = await File.ReadAllBytesAsync(files.JournalPath);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => JournalOriginalFailureCut.CommitHealthy(store));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.UnknownWriteOutcome);
        await Assert.That(failure.Message).IsEqualTo(ZoneTreePersistenceFormat.UnknownWriteOutcome);
        await Assert.That(failure.InnerException is ObjectDisposedException).IsTrue();
        await Assert.That(runtime.Poisoned).IsTrue();
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(OrleansSerialization.Serialize(store.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
        await Assert.That((await File.ReadAllBytesAsync(files.IdentityPath)).AsSpan().SequenceEqual(identityFile)).IsTrue();
        await Assert.That((await File.ReadAllBytesAsync(files.JournalPath)).AsSpan().SequenceEqual(journal)).IsTrue();
        await RequirePoisonedAsync(store);
        await Assert.That((await File.ReadAllBytesAsync(files.JournalPath)).AsSpan().SequenceEqual(journal)).IsTrue();
    }

    private static async Task RequirePoisonedAsync(ZoneTreeStore store)
    {
        var readInvoked = false;
        var read = Assert.ThrowsExactly<KeyLoadException>(() => store.Read(_ =>
        {
            readInvoked = true;
            return true;
        }));
        await RequireRecoveryAsync(read);
        await Assert.That(readInvoked).IsFalse();
        var compileInvoked = false;
        var write = Assert.ThrowsExactly<KeyLoadException>(() => store.Commit((_, _) =>
        {
            compileInvoked = true;
            return true;
        }));
        await RequireRecoveryAsync(write);
        await Assert.That(compileInvoked).IsFalse();
    }

    private static async Task RequireRecoveryAsync(KeyLoadException error)
    {
        await Assert.That(error.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(error.Message).IsEqualTo(ZoneTreePersistenceFormat.RecoveryRequired);
    }
}
