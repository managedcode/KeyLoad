using KeyLoad.Server.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextProjectionOwnershipTests
{
    internal const string Collection = "native-text-ownership";
    internal const string Query = "needle";
    internal const string TextPath = "/text";

    [Test]
    public async Task UntrackedPlausibleNativeEntryFailsAndPreservesGenerationAndBytes()
    {
        using var database = new TestDatabase();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var generation = NativeTextOwnershipFixture.CreateGeneration(database, cancellationToken);
        var root = Directory.GetParent(generation)?.FullName
            ?? throw new InvalidOperationException("The native generation has no manager root.");
        var native = Path.Combine(generation, NativeTextProtocol.NativeDirectory);
        var actualNativeFile = Directory.EnumerateFiles(native, "*", SearchOption.AllDirectories).First();
        var untrackedDirectory = Path.Combine(native, "untracked-layout");
        var untrackedFile = Path.Combine(untrackedDirectory, Path.GetFileName(actualNativeFile));
        var untrackedBytes = new byte[] { 0x41, 0x52, 0x63, 0x74 };
        var manifestPath = Path.Combine(generation, NativeTextProtocol.ManifestFile);
        var manifestBefore = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
        Directory.CreateDirectory(untrackedDirectory);
        await File.WriteAllBytesAsync(untrackedFile, untrackedBytes, cancellationToken);
        var snapshot = await NativeTextOwnershipSnapshot.CaptureAsync(root, cancellationToken);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => NativeTextOwnershipFixture.OpenForRestart(root,
            database));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(Directory.Exists(untrackedDirectory)).IsTrue();
        var preservedOrphan = await File.ReadAllBytesAsync(untrackedFile, cancellationToken);
        var preservedManifest = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
        await Assert.That(preservedOrphan).IsEquivalentTo(untrackedBytes, CollectionOrdering.Matching);
        await Assert.That(preservedManifest).IsEquivalentTo(manifestBefore, CollectionOrdering.Matching);
        await NativeTextOwnershipSnapshot.AssertUnchangedAsync(root, snapshot, cancellationToken);
        await Assert.That(Directory.Exists(generation)).IsTrue();
    }

    [Test]
    public async Task MalformedPendingManifestFailsClosedAndPreservesEveryGenerationEntry()
    {
        using var database = new TestDatabase();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var generation = NativeTextOwnershipFixture.CreateGeneration(database, cancellationToken);
        var root = Directory.GetParent(generation)?.FullName
            ?? throw new InvalidOperationException("The native generation has no manager root.");
        var manifestPath = Path.Combine(generation, NativeTextProtocol.ManifestFile);
        var pendingPath = Path.Combine(generation, NativeTextProtocol.PendingManifestFile);
        var malformed = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
        malformed[^1] ^= 0x01;
        File.Delete(manifestPath);
        await File.WriteAllBytesAsync(pendingPath, malformed, cancellationToken);
        var snapshot = await NativeTextOwnershipSnapshot.CaptureAsync(root, cancellationToken);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => NativeTextOwnershipFixture.OpenForRestart(root,
            database));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(File.Exists(manifestPath)).IsFalse();
        var preservedPending = await File.ReadAllBytesAsync(pendingPath, cancellationToken);
        await Assert.That(preservedPending).IsEquivalentTo(malformed, CollectionOrdering.Matching);
        await NativeTextOwnershipSnapshot.AssertUnchangedAsync(root, snapshot, cancellationToken);
        await Assert.That(Directory.Exists(generation)).IsTrue();
    }

    [Test]
    public async Task UnknownRootEntryPreflightPreservesRecognizedGenerationBeforeAnyRetirement()
    {
        using var database = new TestDatabase();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var generation = NativeTextOwnershipFixture.CreateGeneration(database, cancellationToken);
        var root = Directory.GetParent(generation)?.FullName
            ?? throw new InvalidOperationException("The native generation has no manager root.");
        var manifestPath = Path.Combine(generation, NativeTextProtocol.ManifestFile);
        var manifestBefore = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
        var unknownPath = Path.Combine(root, "untracked-owner-entry.bin");
        var unknownBytes = new byte[] { 0x25, 0x36, 0x47 };
        await File.WriteAllBytesAsync(unknownPath, unknownBytes, cancellationToken);
        var snapshot = await NativeTextOwnershipSnapshot.CaptureAsync(root, cancellationToken);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => NativeTextOwnershipFixture.OpenForRestart(root,
            database));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        var preservedUnknown = await File.ReadAllBytesAsync(unknownPath, cancellationToken);
        var preservedManifest = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
        await Assert.That(preservedUnknown).IsEquivalentTo(unknownBytes, CollectionOrdering.Matching);
        await Assert.That(preservedManifest).IsEquivalentTo(manifestBefore, CollectionOrdering.Matching);
        await NativeTextOwnershipSnapshot.AssertUnchangedAsync(root, snapshot, cancellationToken);
        await Assert.That(Directory.Exists(generation)).IsTrue();
    }
}
