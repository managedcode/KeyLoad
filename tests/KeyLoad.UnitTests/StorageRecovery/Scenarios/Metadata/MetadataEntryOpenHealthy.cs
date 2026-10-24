using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.StorageRecovery;

internal static class MetadataEntryOpenHealthy
{
    private const int ColdCycles = 2;
    private const long CommittedPosition = 1;
    private const int OneUnderByte = 1;
    private const string MissingSuffix = ".missing";

    internal static async Task BoundsAsync(MetadataEntryOpenOwner owner)
    {
        var files = owner.Files;
        var original = await files.CaptureStoreEntriesAsync();
        var bytes = await File.ReadAllBytesAsync(files.IdentityPath);
        var buffer = UnitExecutionOptions.StorageExecution().Value.StreamBufferBytes;
        var exact = ZoneTreeMetadataFile.Read(files.IdentityPath, bytes.Length, buffer,
            ZoneTreePersistenceFormat.IdentityFormatUnsupported);
        await Assert.That(exact.Span.SequenceEqual(bytes)).IsTrue();
        var under = Assert.ThrowsExactly<KeyLoadException>(() => ZoneTreeMetadataFile.Read(
            files.IdentityPath, bytes.Length - OneUnderByte, buffer, ZoneTreePersistenceFormat.IdentityFormatUnsupported));
        await Assert.That(under.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(under.Message).IsEqualTo(ZoneTreePersistenceFormat.IdentityFormatUnsupported);
        var missing = files.IdentityPath + MissingSuffix;
        Assert.ThrowsExactly<FileNotFoundException>(() => ZoneTreeMetadataFile.Read(
            missing, bytes.Length, buffer, ZoneTreePersistenceFormat.IdentityFormatUnsupported));
        await Assert.That(File.Exists(missing)).IsFalse();
        await files.AssertStoreEntriesUnchangedAsync(original);
        await ReopenAsync(owner);
    }

    internal static async Task ReopenAsync(MetadataEntryOpenOwner owner)
    {
        var files = owner.Files;
        var failures = new List<Exception>();
        for (var cold = 0; cold < ColdCycles && owner.CanAdvance; cold++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var result = await owner.InspectAsync();
                await ExistingStoreInspectionAssertions.SucceededAsync(result, files.Identity, files.Value, CommittedPosition);
            }, failures);
        }
        ZoneTreeStore? store = null;
        if (owner.CanAdvance)
        {
            store = owner.OpenStore(failures);
        }
        if (store is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(OrleansSerialization.Serialize(store.Identity))
                    .IsEquivalentTo(OrleansSerialization.Serialize(files.Identity), CollectionOrdering.Matching);
                await Assert.That(store.Position).IsEqualTo(CommittedPosition);
                await Assert.That(store.Read(view => view.ReadOwnedValue(files.Key)))
                    .IsEquivalentTo(files.Value, CollectionOrdering.Matching);
            }, failures);
            owner.CloseStore(store, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
