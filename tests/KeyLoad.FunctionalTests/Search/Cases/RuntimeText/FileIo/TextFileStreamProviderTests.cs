using KeyLoad.Server.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextFileStreamProviderTests
{
    [Test]
    public async Task ReplaceWithoutBackupPreservesReplaceSemantics()
    {
        using var database = new TestDatabase();
        var (provider, _, execution) = CreateProvider(database);
        var source = Path.Combine(execution, "source.bin");
        var destination = Path.Combine(execution, "destination.bin");
        var sourceBytes = new byte[] { 0x10, 0x20, 0x30 };
        var previousBytes = new byte[] { 0x40, 0x50 };
        WriteOwned(provider, source, sourceBytes);
        WriteOwned(provider, destination, previousBytes);
        var filesBefore = Directory.EnumerateFiles(execution, "*", SearchOption.AllDirectories).Count();

        provider.Replace(source, destination, null);

        await AssertBytesAsync(destination, sourceBytes);
        await Assert.That(File.Exists(source)).IsFalse();
        await Assert.That(Directory.EnumerateFiles(execution, "*", SearchOption.AllDirectories).Count())
            .IsEqualTo(filesBefore - 1);
    }

    [Test]
    public async Task ReplaceWithOwnedBackupPreservesPreviousDestination()
    {
        using var database = new TestDatabase();
        var (provider, _, execution) = CreateProvider(database);
        var source = Path.Combine(execution, "replacement.bin");
        var destination = Path.Combine(execution, "current.bin");
        var backup = Path.Combine(execution, "previous.bin");
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
        var (provider, root, execution) = CreateProvider(database);
        var source = Path.Combine(execution, "source.bin");
        var destination = Path.Combine(execution, "destination.bin");
        var unowned = Path.Combine(execution, "unowned-backup.bin");
        var outside = Path.Combine(database.Directory, "outside-backup.bin");
        var sourceBytes = new byte[] { 0x21, 0x32, 0x43 };
        var destinationBytes = new byte[] { 0x54, 0x65 };
        var unownedBytes = new byte[] { 0x76, 0x87 };
        var outsideBytes = new byte[] { 0x98, 0xa9 };
        WriteOwned(provider, source, sourceBytes);
        WriteOwned(provider, destination, destinationBytes);
        await File.WriteAllBytesAsync(unowned, unownedBytes, TestContext.Current!.Execution.CancellationToken);
        await File.WriteAllBytesAsync(outside, outsideBytes, TestContext.Current!.Execution.CancellationToken);
        var before = await TextOwnershipSnapshot.CaptureAsync(root,
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
        await TextOwnershipSnapshot.AssertUnchangedAsync(root, before,
            TestContext.Current!.Execution.CancellationToken);
    }

    private static (TextFileStreamProvider Provider, string Root, string Execution) CreateProvider(
        TestDatabase database)
    {
        var generation = TextOwnershipFixture.CreateGeneration(database,
            TestContext.Current!.Execution.CancellationToken);
        var root = Directory.GetParent(generation)?.FullName
            ?? throw new InvalidOperationException("The execution generation has no manager root.");
        var leaf = Path.GetFileName(generation);
        var execution = Path.Combine(generation, TextProtocol.RuntimeDirectoryCore);
        var options = UnitTextOptions.Execution();
        var resources = new TextResourceOwnership(options);
        resources.RegisterRoot(root, _ => TextRootFiles.VerifyReceipt(root, database.Store.Identity.NodeId, options));
        return (new TextFileStreamProvider(root, leaf, database.Store.Identity.NodeId, options, resources), root, execution);
    }

    private static void WriteOwned(TextFileStreamProvider provider, string path, byte[] value)
    {
        using var file = provider.CreateFileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.ToStream().Write(value);
    }

    [Test]
    public async Task DurableMetadataCreationMovesOwnedBytesWithoutReplacingExistingContent()
    {
        using var database = new TestDatabase();
        var (provider, root, execution) = CreateProvider(database);
        var destination = Path.Combine(execution, "metadata.bin");
        var expected = new byte[] { 0x10, 0x00, 0xFF, 0x80 };
        provider.GetDurableFileWriter().WriteNewFile(destination, expected);
        await AssertBytesAsync(destination, expected);
        await Assert.That(File.Exists(destination + ".tmp")).IsFalse();
        var before = await TextOwnershipSnapshot.CaptureAsync(root,
            TestContext.Current!.Execution.CancellationToken);

        Assert.ThrowsExactly<IOException>(() => provider.GetDurableFileWriter().WriteNewFile(destination, [0x20]));

        await AssertBytesAsync(destination, expected);
        await TextOwnershipSnapshot.AssertUnchangedAsync(root, before,
            TestContext.Current!.Execution.CancellationToken);
        var healthy = Path.Combine(execution, "healthy-metadata.bin");
        provider.GetDurableFileWriter().WriteNewFile(healthy, expected);
        await AssertBytesAsync(healthy, expected);
    }

    [Test]
    public async Task MoveRejectsOccupiedOutsideAndUnownedTargetsBeforeChangingSourceOrLedger()
    {
        using var database = new TestDatabase();
        var (provider, root, execution) = CreateProvider(database);
        var source = Path.Combine(execution, "move-source.bin");
        var destination = Path.Combine(execution, "move-occupied.bin");
        var unowned = Path.Combine(execution, "move-unowned.bin");
        var outside = Path.Combine(database.Directory, "move-outside.bin");
        var sourceBytes = new byte[] { 0x21, 0x32 };
        var occupiedBytes = new byte[] { 0x54, 0x65 };
        WriteOwned(provider, source, sourceBytes);
        WriteOwned(provider, destination, occupiedBytes);
        await File.WriteAllBytesAsync(unowned, occupiedBytes, TestContext.Current!.Execution.CancellationToken);
        await File.WriteAllBytesAsync(outside, occupiedBytes, TestContext.Current!.Execution.CancellationToken);
        var before = await TextOwnershipSnapshot.CaptureAsync(root,
            TestContext.Current!.Execution.CancellationToken);

        Assert.ThrowsExactly<IOException>(() => provider.MoveFile(source, destination));
        foreach (var target in new[] { outside, unowned })
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => provider.MoveFile(source, target)).Code)
                .IsEqualTo(ErrorCode.FormatUnsupported);
        }
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => provider.MoveFile(outside, source)).Code)
            .IsEqualTo(ErrorCode.FormatUnsupported);

        await AssertBytesAsync(source, sourceBytes);
        foreach (var occupied in new[] { destination, unowned, outside })
        {
            await AssertBytesAsync(occupied, occupiedBytes);
        }
        await TextOwnershipSnapshot.AssertUnchangedAsync(root, before,
            TestContext.Current!.Execution.CancellationToken);
        File.Delete(unowned);
        var healthy = Path.Combine(execution, "move-healthy.bin");
        provider.MoveFile(source, healthy);
        await AssertBytesAsync(healthy, sourceBytes);
        await Assert.That(File.Exists(source)).IsFalse();
    }

    private static async Task AssertBytesAsync(string path, byte[] expected)
    {
        var actual = await File.ReadAllBytesAsync(path, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(actual).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }
}
