using System.Text.Json;
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
        var identity = await ReadIdentity(fixture);
        var unknown = identity[..^MetadataTestContract.OneByte] + MetadataTestContract.JsonMemberSeparator
            + MetadataTestContract.UnknownJsonBooleanMember
            + MetadataTestContract.JsonObjectClose;
        await AssertIdentityJsonFailure(fixture, unknown);
    }

    [Test]
    public async Task DeeplyNestedIdentityJsonIsRejectedWithValidWalAndRefreshedOuterMetadata()
    {
        using var fixture = new MetadataBackupFixture();
        var identity = await ReadIdentity(fixture);
        var nested = new string('[', MetadataTestContract.NestedDepth) + MetadataTestContract.NestedJsonScalar
            + new string(']', MetadataTestContract.NestedDepth);
        var deepUnknown = identity[..^MetadataTestContract.OneByte] + MetadataTestContract.JsonMemberSeparator
            + MetadataTestContract.UnknownJsonMemberPrefix + nested + MetadataTestContract.JsonObjectClose;
        await AssertIdentityJsonFailure(fixture, deepUnknown);
    }

    private static async Task<string> ReadIdentity(MetadataBackupFixture fixture) =>
        await File.ReadAllTextAsync(Path.Combine(fixture.BackupDirectory,
            MetadataTestContract.IdentityFileName));

    private static async Task AssertIdentityNullFailure(MetadataBackupFixture fixture, string content)
    {
        await PrepareIdentity(fixture, content);
        var destination = Path.Combine(fixture.BackupDirectory, MetadataTestContract.IdentityJsonRestorePath);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            ZoneTreeStore.Restore(fixture.BackupDirectory, destination));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(Directory.Exists(destination)).IsFalse();
    }

    private static async Task AssertIdentityJsonFailure(MetadataBackupFixture fixture, string content)
    {
        await PrepareIdentity(fixture, content);
        var destination = Path.Combine(fixture.BackupDirectory, MetadataTestContract.IdentityJsonRestorePath);
        var failure = Assert.ThrowsExactly<JsonException>(() =>
            ZoneTreeStore.Restore(fixture.BackupDirectory, destination));

        await Assert.That(failure).IsNotNull();
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
