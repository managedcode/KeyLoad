using KeyLoad.Server.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextFileStreamProviderTests
{
    [Test]
    public async Task ReplaceWithoutBackupPreservesNativeReplaceSemantics()
    {
        using var database = new TestDatabase();
        var (provider, _, native) = CreateProvider(database);
        var source = Path.Combine(native, "source.bin");
        var destination = Path.Combine(native, "destination.bin");
        var sourceBytes = new byte[] { 0x10, 0x20, 0x30 };
        var previousBytes = new byte[] { 0x40, 0x50 };
        WriteOwned(provider, source, sourceBytes);
        WriteOwned(provider, destination, previousBytes);
        var filesBefore = Directory.EnumerateFiles(native, "*", SearchOption.AllDirectories).Count();

        provider.Replace(source, destination, null);

        await AssertBytesAsync(destination, sourceBytes);
        await Assert.That(File.Exists(source)).IsFalse();
        await Assert.That(Directory.EnumerateFiles(native, "*", SearchOption.AllDirectories).Count())
            .IsEqualTo(filesBefore - 1);
    }

    [Test]
    public async Task ReplaceWithOwnedBackupPreservesPreviousDestination()
    {
        using var database = new TestDatabase();
        var (provider, _, native) = CreateProvider(database);
        var source = Path.Combine(native, "replacement.bin");
        var destination = Path.Combine(native, "current.bin");
        var backup = Path.Combine(native, "previous.bin");
        var sourceBytes = new byte[] { 0x61, 0x72, 0x83 };
        var previousBytes = new byte[] { 0x94, 0xa5 };
        WriteOwned(provider, source, sourceBytes);
        WriteOwned(provider, destination, previousBytes);

        provider.Replace(source, destination, backup);

        await AssertBytesAsync(destination, sourceBytes);
        await AssertBytesAsync(backup, previousBytes);
        await Assert.That(File.Exists(source)).IsFalse();
    }

    [Test]
    public async Task OutsideAndUnownedBackupsAreRejectedWithoutChangingFilesOrLedger()
    {
        using var database = new TestDatabase();
        var (provider, root, native) = CreateProvider(database);
        var source = Path.Combine(native, "source.bin");
        var destination = Path.Combine(native, "destination.bin");
        var unowned = Path.Combine(native, "unowned-backup.bin");
        var outside = Path.Combine(database.Directory, "outside-backup.bin");
        var sourceBytes = new byte[] { 0x21, 0x32, 0x43 };
        var destinationBytes = new byte[] { 0x54, 0x65 };
        var unownedBytes = new byte[] { 0x76, 0x87 };
        var outsideBytes = new byte[] { 0x98, 0xa9 };
        WriteOwned(provider, source, sourceBytes);
        WriteOwned(provider, destination, destinationBytes);
        await File.WriteAllBytesAsync(unowned, unownedBytes, TestContext.Current!.Execution.CancellationToken);
        await File.WriteAllBytesAsync(outside, outsideBytes, TestContext.Current!.Execution.CancellationToken);
        var before = await NativeTextOwnershipSnapshot.CaptureAsync(root,
            TestContext.Current!.Execution.CancellationToken);

        var outsideFailure = Assert.ThrowsExactly<KeyLoadException>(() =>
            provider.Replace(source, destination, outside));
        await Assert.That(outsideFailure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        var unownedFailure = Assert.ThrowsExactly<KeyLoadException>(() =>
            provider.Replace(source, destination, unowned));
        await Assert.That(unownedFailure.Code).IsEqualTo(ErrorCode.FormatUnsupported);

        await AssertBytesAsync(source, sourceBytes);
        await AssertBytesAsync(destination, destinationBytes);
        await AssertBytesAsync(unowned, unownedBytes);
        await AssertBytesAsync(outside, outsideBytes);
        await NativeTextOwnershipSnapshot.AssertUnchangedAsync(root, before,
            TestContext.Current!.Execution.CancellationToken);
    }

    private static (NativeTextFileStreamProvider Provider, string Root, string Native) CreateProvider(
        TestDatabase database)
    {
        var generation = NativeTextOwnershipFixture.CreateGeneration(database,
            TestContext.Current!.Execution.CancellationToken);
        var root = Directory.GetParent(generation)?.FullName
            ?? throw new InvalidOperationException("The native generation has no manager root.");
        var leaf = Path.GetFileName(generation);
        var native = Path.Combine(generation, NativeTextProtocol.NativeDirectory);
        return (new NativeTextFileStreamProvider(root, leaf, database.Store.Identity.NodeId, UnitNativeTextOptions.Execution()), root, native);
    }

    private static void WriteOwned(NativeTextFileStreamProvider provider, string path, byte[] value)
    {
        using var file = provider.CreateFileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.ToStream().Write(value);
    }

    private static async Task AssertBytesAsync(string path, byte[] expected)
    {
        var actual = await File.ReadAllBytesAsync(path, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(actual).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }
}
