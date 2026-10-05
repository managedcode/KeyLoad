using KeyLoad.AppHost.Features.ClusterReplication.Commands;
using KeyLoad.AppHost.Hosting;
using KeyLoad.UnitTests.Features.ClusterRouting.Helpers;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ClusterProfileUpgradeCommandTests
{
    [Test]
    public async Task ExplicitCommandUpgradesRealLegacyProfileThroughAppHostEntryAndRetainsVerifiedBackup()
    {
        using var fixture = await OfflineLegacyProfileFixture.CreateAsync();
        var originalMode = ReadMode(fixture.ProfilePath);
        var exitCode = await KeyLoadAppHostApplication.RunAsync(
            [ClusterProfileUpgradeCommand.UpgradeFlag, ClusterProfileUpgradeCommand.DataRootFlag, fixture.Root]);
        var upgraded = ClusterProfileStore.Open(fixture.Root);
        var backup = Path.Combine(fixture.Root, ClusterProfileStore.LegacyBackupName);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(upgraded.Version).IsEqualTo(2);
        await Assert.That(upgraded.PhysicalShardId).IsNotEqualTo(Guid.Empty);
        await Assert.That(upgraded.Incarnation).IsEqualTo(OfflineLegacyProfileFixture.Incarnation);
        await Assert.That(upgraded.SigningKey).IsEqualTo(OfflineLegacyProfileFixture.ExpectedSigningKey);
        await Assert.That(upgraded.PeerSecret).IsEqualTo(OfflineLegacyProfileFixture.ExpectedPeerSecret);
        await Assert.That(upgraded.AdminKey).IsEqualTo(OfflineLegacyProfileFixture.ExpectedAdminKey);
        await Assert.That(BytesEqual(await File.ReadAllBytesAsync(backup), fixture.Original)).IsTrue();
        await AssertModePreservedAsync(fixture.ProfilePath, originalMode);
        await AssertModePreservedAsync(backup, originalMode);
        await Assert.That(Directory.GetFiles(fixture.Root, "*.tmp-*", SearchOption.TopDirectoryOnly).Length).IsEqualTo(0);
    }

    [Test]
    public async Task PartialMixedRelativeAndMalformedUpgradeArgumentsFailBeforeChangingProfile()
    {
        using var fixture = await OfflineLegacyProfileFixture.CreateAsync();
        string[][] invalidArguments =
        [
            [ClusterProfileUpgradeCommand.UpgradeFlag],
            [ClusterProfileUpgradeCommand.UpgradeFlag, ClusterProfileUpgradeCommand.DataRootFlag, "relative-root"],
            [ClusterProfileUpgradeCommand.UpgradeFlag, ClusterProfileUpgradeCommand.DataRootFlag,
                Path.Combine(fixture.Root, "missing-root")],
            [ClusterProfileUpgradeCommand.UpgradeFlag, ClusterProfileUpgradeCommand.DataRootFlag, fixture.Root,
                "--KeyLoadTests:Suite=unit"],
            ["--keyload-profile-upgrade-v1-to-v3", ClusterProfileUpgradeCommand.DataRootFlag, fixture.Root],
            [ClusterProfileUpgradeCommand.DataRootFlag, fixture.Root]
        ];

        foreach (var arguments in invalidArguments)
        {
            var exitCode = await KeyLoadAppHostApplication.RunAsync(arguments);
            await Assert.That(exitCode).IsEqualTo(2);
            await Assert.That(BytesEqual(await File.ReadAllBytesAsync(fixture.ProfilePath), fixture.Original)).IsTrue();
            await Assert.That(File.Exists(Path.Combine(fixture.Root, ClusterProfileStore.LegacyBackupName))).IsFalse();
        }
    }

    [Test]
    public async Task ExistingBackupMakesRealCommandFailWithoutReplacingEitherFile()
    {
        using var fixture = await OfflineLegacyProfileFixture.CreateAsync();
        var backupBytes = "preserved backup"u8.ToArray();
        await File.WriteAllBytesAsync(fixture.BackupPath, backupBytes);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(fixture.BackupPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }

        var exitCode = await KeyLoadAppHostApplication.RunAsync(
            [ClusterProfileUpgradeCommand.UpgradeFlag, ClusterProfileUpgradeCommand.DataRootFlag, fixture.Root]);

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(BytesEqual(await File.ReadAllBytesAsync(fixture.ProfilePath), fixture.Original)).IsTrue();
        await Assert.That(BytesEqual(await File.ReadAllBytesAsync(fixture.BackupPath), backupBytes)).IsTrue();
        await Assert.That(Directory.GetFiles(fixture.Root, "*.tmp-*", SearchOption.TopDirectoryOnly).Length).IsEqualTo(0);
    }

    private static UnixFileMode? ReadMode(string path)
        => OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(path);

    private static bool BytesEqual(byte[] actual, byte[] expected) => actual.AsSpan().SequenceEqual(expected);

    private static async Task AssertModePreservedAsync(string path, UnixFileMode? mode)
    {
        if (!OperatingSystem.IsWindows() && mode is { } expected)
        { await Assert.That(File.GetUnixFileMode(path)).IsEqualTo(expected); }
    }
}
