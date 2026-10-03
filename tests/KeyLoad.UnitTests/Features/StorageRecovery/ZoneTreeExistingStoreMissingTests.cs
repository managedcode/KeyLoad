using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class ZoneTreeExistingStoreMissingTests
{
    [Test]
    [Arguments(ZoneTreeExistingStoreFixture.OwnerFile)]
    [Arguments(ZoneTreeExistingStoreFixture.IdentityFile)]
    [Arguments(ZoneTreeExistingStoreFixture.JournalFile)]
    public async Task AcSg009001MissingOriginalFileIsRejectedWithoutReplacement(string name)
    {
        using var files = new ZoneTreeExistingStoreFixture();
        var identityBytes = await File.ReadAllBytesAsync(files.IdentityPath);
        var missing = files.HideFile(name);
        var result = await files.InspectAsync();
        await ExistingStoreInspectionAssertions.FailedAsync(result, null, ExistingStoreInspectionExpectedFailures.FileNotFound);
        await Assert.That(File.Exists(missing)).IsFalse();
        var retainedIdentity = name == ZoneTreeExistingStoreFixture.IdentityFile ? files.HiddenIdentityPath : files.IdentityPath;
        await Assert.That(await File.ReadAllBytesAsync(retainedIdentity)).IsEquivalentTo(identityBytes, CollectionOrdering.Matching);
        if (name != ZoneTreeExistingStoreFixture.OwnerFile)
        {
            await files.AssertOwnerAvailableAsync();
        }
    }

    [Test]
    public async Task AcSg009001MissingCanonicalDirectoryIsNotCreated()
    {
        using var files = new ZoneTreeExistingStoreFixture();
        ZoneTreeExistingStoreFixture.HideDirectory(files.DirectoryPath);
        var result = await files.InspectAsync();
        await ExistingStoreInspectionAssertions.FailedAsync(result, null, ExistingStoreInspectionExpectedFailures.DirectoryNotFound);
        await Assert.That(Directory.Exists(files.DirectoryPath)).IsFalse();
    }

    [Test]
    public async Task AcSg009001MissingOriginalTreeIsNotCreated()
    {
        using var files = new ZoneTreeExistingStoreFixture();
        var identityBytes = await File.ReadAllBytesAsync(files.IdentityPath);
        var tree = Path.Combine(files.DirectoryPath, ZoneTreeExistingStoreFixture.TreeDirectory);
        ZoneTreeExistingStoreFixture.HideDirectory(tree);
        var result = await files.InspectAsync();
        await ExistingStoreInspectionAssertions.FailedAsync(result, null, ExistingStoreInspectionExpectedFailures.DirectoryNotFound);
        await Assert.That(Directory.Exists(tree)).IsFalse();
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath)).IsEquivalentTo(identityBytes, CollectionOrdering.Matching);
        await files.AssertOwnerAvailableAsync();
    }

    [Test]
    public async Task AcSg009001MissingProviderMetadataInExistingTreeIsNotRecreated()
    {
        using var files = new ZoneTreeExistingStoreFixture();
        var identityBytes = await File.ReadAllBytesAsync(files.IdentityPath);
        files.HideProviderMetadata();
        var result = await files.InspectAsync();
        await ExistingStoreInspectionAssertions.FailedAsync(result, null, ExistingStoreInspectionExpectedFailures.DatabaseNotFound);
        var tree = Path.Combine(files.DirectoryPath, ZoneTreeExistingStoreFixture.TreeDirectory);
        await Assert.That(Directory.GetFileSystemEntries(tree)).IsEmpty();
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath)).IsEquivalentTo(identityBytes, CollectionOrdering.Matching);
        await files.AssertOwnerAvailableAsync();
    }

    [Test]
    public async Task AcSg009001OccupiedCanonicalOwnerRetainsOriginalIdentity()
    {
        using var files = new ZoneTreeExistingStoreFixture();
        var identityBytes = await File.ReadAllBytesAsync(files.IdentityPath);
        await using (var originalOwner = new FileStream(Path.Combine(files.DirectoryPath, ZoneTreeExistingStoreFixture.OwnerFile),
                         FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = await files.InspectAsync();
            await ExistingStoreInspectionAssertions.FailedAsync(result, null, ExistingStoreInspectionExpectedFailures.Io);
            await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath)).IsEquivalentTo(identityBytes, CollectionOrdering.Matching);
        }
        await files.AssertOwnerAvailableAsync();
    }
}
