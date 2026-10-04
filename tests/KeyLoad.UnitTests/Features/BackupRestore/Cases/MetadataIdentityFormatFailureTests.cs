using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class MetadataIdentityFormatFailureTests
{
    [Test]
    public async Task NullIdentityFailsWithValidWalAndRefreshedOuterMetadata()
    {
        using var fixture = new MetadataBackupFixture();
        await AssertIdentityNullFailure(fixture, MetadataTestContract.IdentityNullJson);
    }

    [Test]
    public async Task MalformedIdentityFailsWithValidWalAndRefreshedOuterMetadata()
    {
        using var fixture = new MetadataBackupFixture();
        await AssertIdentityJsonFailure(fixture, MetadataTestContract.MalformedJson);
    }

    [Test]
    public async Task UnknownIdentityMemberFailsWithValidWalAndRefreshedOuterMetadata()
    {
        using var fixture = new MetadataBackupFixture();
        var unknown = MetadataTestContract.LegacyUnknownObjectJson;
        await AssertIdentityJsonFailure(fixture, unknown);
    }

    [Test]
    public async Task DeeplyNestedIdentityJsonIsRejectedWithValidWalAndRefreshedOuterMetadata()
    {
        using var fixture = new MetadataBackupFixture();
        var nested = new string('[', MetadataTestContract.NestedDepth) + MetadataTestContract.NestedJsonScalar
            + new string(']', MetadataTestContract.NestedDepth);
        var deepUnknown = MetadataTestContract.JsonObjectOpen + MetadataTestContract.UnknownJsonMemberPrefix
            + nested + MetadataTestContract.JsonObjectClose;
        await AssertIdentityJsonFailure(fixture, deepUnknown);
    }

    private static async Task AssertIdentityNullFailure(MetadataBackupFixture fixture, string content)
    {
        await PrepareIdentity(fixture, content);
        var destination = Path.Combine(fixture.BackupDirectory, MetadataTestContract.IdentityJsonRestorePath);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            ZoneTreeStore.Restore(fixture.BackupDirectory, destination));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(Directory.Exists(destination)).IsFalse();
    }

    private static async Task AssertIdentityJsonFailure(MetadataBackupFixture fixture, string content)
    {
        await PrepareIdentity(fixture, content);
        var destination = Path.Combine(fixture.BackupDirectory, MetadataTestContract.IdentityJsonRestorePath);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            ZoneTreeStore.Restore(fixture.BackupDirectory, destination));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(Directory.Exists(destination)).IsFalse();
    }

    private static async Task PrepareIdentity(MetadataBackupFixture fixture, string content)
    {
        await File.WriteAllTextAsync(Path.Combine(fixture.BackupDirectory,
            MetadataTestContract.IdentityFileName), content);
        await MetadataTestFiles.UpdateManifestFileAsync(fixture.BackupDirectory,
            MetadataTestContract.IdentityFileName);
    }
}
