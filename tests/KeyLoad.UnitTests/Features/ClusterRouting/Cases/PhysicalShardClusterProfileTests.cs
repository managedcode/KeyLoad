using System.Text;
using System.Text.Json;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PhysicalShardClusterProfileTests
{
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const string SigningKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
    private const string PeerSecret = "ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8=";
    private const string AdminKey = "root.0123456789abcdef0123456789abcdef";
    private static readonly Guid Incarnation = Guid.Parse("5cb3e3f8-7d6f-4fb4-b0f5-801c93f3f5bb");

    [Test]
    public async Task NewProfileHasExactV2FieldsAndStableOpaqueIdentityAcrossReopen()
    {
        using var fixture = ProfileFixture.Create();
        var first = ClusterProfileStore.Open(fixture.Root, UnitProfileOptions.Execution());
        var bytes = await File.ReadAllBytesAsync(fixture.ProfilePath);
        var reopened = ClusterProfileStore.Open(fixture.Root, UnitProfileOptions.Execution());
        await AssertProfileFieldsAsync(bytes);
        await Assert.That(first.Version).IsEqualTo(2);
        await Assert.That(first.PhysicalShardId).IsNotEqualTo(Guid.Empty);
        await Assert.That(first.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(reopened).IsEqualTo(first);
        await Assert.That(BytesEqual(await File.ReadAllBytesAsync(fixture.ProfilePath), bytes)).IsTrue();
        await AssertPrivateModeAsync(fixture.ProfilePath, PrivateFile);
        await Assert.That(TemporaryFiles(fixture.Root).Length).IsEqualTo(0);
    }

    [Test]
    public async Task NormalOpenRejectsLegacyProfileWithoutChangingBytesPermissionsOrCreatingBackup()
    {
        using var fixture = ProfileFixture.Create();
        var original = await fixture.WriteLegacyAsync();
        var originalMode = ReadMode(fixture.ProfilePath);
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => ClusterProfileStore.Open(fixture.Root, UnitProfileOptions.Execution()));
        await Assert.That(error.Message).IsEqualTo("The private cluster profile is missing required identity or credential fields, or is invalid.");
        await Assert.That(BytesEqual(await File.ReadAllBytesAsync(fixture.ProfilePath), original)).IsTrue();
        await AssertModePreservedAsync(fixture.ProfilePath, originalMode);
        await Assert.That(File.Exists(Path.Combine(fixture.Root, ClusterProfileStore.LegacyBackupName))).IsFalse();
        await Assert.That(TemporaryFiles(fixture.Root).Length).IsEqualTo(0);
    }

    [Test]
    public async Task ExplicitOfflineUpgradePreservesLegacyIdentityCredentialsAndPermissionsWithVerifiedBackup()
    {
        using var fixture = ProfileFixture.Create();
        var original = await fixture.WriteLegacyAsync();
        var originalMode = ReadMode(fixture.ProfilePath);
        var upgraded = ClusterProfileStore.UpgradeLegacyOffline(fixture.Root, UnitProfileOptions.Execution());
        var backup = Path.Combine(fixture.Root, ClusterProfileStore.LegacyBackupName);
        var reopened = ClusterProfileStore.Open(fixture.Root, UnitProfileOptions.Execution());
        await AssertProfileFieldsAsync(await File.ReadAllBytesAsync(fixture.ProfilePath));
        await Assert.That(upgraded.Version).IsEqualTo(2);
        await Assert.That(upgraded.PhysicalShardId).IsNotEqualTo(Guid.Empty);
        await Assert.That(upgraded.Incarnation).IsEqualTo(Incarnation);
        await Assert.That(upgraded.SigningKey).IsEqualTo(SigningKey);
        await Assert.That(upgraded.PeerSecret).IsEqualTo(PeerSecret);
        await Assert.That(upgraded.AdminKey).IsEqualTo(AdminKey);
        await Assert.That(reopened).IsEqualTo(upgraded);
        await Assert.That(BytesEqual(await File.ReadAllBytesAsync(backup), original)).IsTrue();
        await AssertModePreservedAsync(fixture.ProfilePath, originalMode);
        await AssertModePreservedAsync(backup, originalMode);
        await Assert.That(TemporaryFiles(fixture.Root).Length).IsEqualTo(0);
    }

    [Test]
    public async Task ReadOnlyPrivateLegacyModeSurvivesUpgradeAndV2Reopen()
    {
        if (OperatingSystem.IsWindows())
        { return; }
        using var fixture = ProfileFixture.Create();
        var original = await fixture.WriteLegacyAsync(UnixFileMode.UserRead);
        var upgraded = ClusterProfileStore.UpgradeLegacyOffline(fixture.Root, UnitProfileOptions.Execution());
        await Assert.That(ClusterProfileStore.Open(fixture.Root, UnitProfileOptions.Execution())).IsEqualTo(upgraded);
        await Assert.That(BytesEqual(await File.ReadAllBytesAsync(Path.Combine(fixture.Root,
            ClusterProfileStore.LegacyBackupName)), original)).IsTrue();
        await Assert.That(File.GetUnixFileMode(fixture.ProfilePath)).IsEqualTo(UnixFileMode.UserRead);
        await Assert.That(File.GetUnixFileMode(Path.Combine(fixture.Root,
            ClusterProfileStore.LegacyBackupName))).IsEqualTo(UnixFileMode.UserRead);
    }

    [Test]
    public async Task MalformedDuplicateMissingUnknownWrongVersionEmptyIdentityAndOversizedProfilesFailClosed()
    {
        foreach (var bytes in PhysicalShardProfileTestData.InvalidProfiles())
        {
            using var fixture = ProfileFixture.Create();
            await File.WriteAllBytesAsync(fixture.ProfilePath, bytes);
            PhysicalShardProfileTestData.SetPrivateFileMode(fixture.ProfilePath);
            var error = Assert.ThrowsExactly<InvalidOperationException>(() => ClusterProfileStore.Open(fixture.Root, UnitProfileOptions.Execution()));
            await Assert.That(error.Message).IsEqualTo("The private cluster profile is missing required identity or credential fields, or is invalid.");
            await Assert.That(BytesEqual(await File.ReadAllBytesAsync(fixture.ProfilePath), bytes)).IsTrue();
            await AssertPrivateModeAsync(fixture.ProfilePath, PrivateFile);
            await Assert.That(TemporaryFiles(fixture.Root).Length).IsEqualTo(0);
        }
    }

    [Test]
    public async Task ExistingBackupAndProfileSymlinkAreRejectedWithoutChangingLegacySource()
    {
        if (OperatingSystem.IsWindows())
        { return; }
        using (var fixture = ProfileFixture.Create())
        {
            var original = await fixture.WriteLegacyAsync();
            var backup = Path.Combine(fixture.Root, ClusterProfileStore.LegacyBackupName);
            var backupBytes = Encoding.UTF8.GetBytes("existing backup");
            await File.WriteAllBytesAsync(backup, backupBytes);
            PhysicalShardProfileTestData.SetPrivateFileMode(backup);
            PhysicalShardProfileTestData.AssertOpenAndUpgradeRejected(fixture.Root);
            await Assert.That(BytesEqual(await File.ReadAllBytesAsync(fixture.ProfilePath), original)).IsTrue();
            await Assert.That(BytesEqual(await File.ReadAllBytesAsync(backup), backupBytes)).IsTrue();
        }
        using (var fixture = ProfileFixture.Create())
        {
            var outside = Path.Combine(fixture.Root, "outside-profile.json");
            var original = await fixture.WriteLegacyAsync();
            File.Move(fixture.ProfilePath, outside);
            File.CreateSymbolicLink(fixture.ProfilePath, outside);
            PhysicalShardProfileTestData.AssertOpenAndUpgradeRejected(fixture.Root);
            await Assert.That(File.Exists(fixture.ProfilePath)).IsTrue();
            await Assert.That(BytesEqual(await File.ReadAllBytesAsync(outside), original)).IsTrue();
            await Assert.That(File.Exists(Path.Combine(fixture.Root, ClusterProfileStore.LegacyBackupName))).IsFalse();
        }
    }

    [Test]
    public async Task GroupReadableAndOwnerWriteOnlyLegacyModesAreRejectedWithoutRewrite()
    {
        if (OperatingSystem.IsWindows())
        { return; }
        foreach (var mode in new[] { UnixFileMode.UserRead | UnixFileMode.GroupRead,
            UnixFileMode.UserRead | UnixFileMode.OtherRead, UnixFileMode.UserWrite })
        {
            using var fixture = ProfileFixture.Create();
            var original = await fixture.WriteLegacyAsync(mode);
            PhysicalShardProfileTestData.AssertOpenAndUpgradeRejected(fixture.Root);
            await Assert.That(File.GetUnixFileMode(fixture.ProfilePath)).IsEqualTo(mode);
            await Assert.That(File.Exists(Path.Combine(fixture.Root, ClusterProfileStore.LegacyBackupName))).IsFalse();
            File.SetUnixFileMode(fixture.ProfilePath, PrivateFile);
            await Assert.That(BytesEqual(await File.ReadAllBytesAsync(fixture.ProfilePath), original)).IsTrue();
        }
    }

    private static async Task AssertProfileFieldsAsync(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        await Assert.That(names.Length).IsEqualTo(6);
        foreach (var name in new[] { "Version", "PhysicalShardId", "Incarnation", "SigningKey", "PeerSecret", "AdminKey" })
        { await Assert.That(names.Contains(name, StringComparer.Ordinal)).IsTrue(); }
    }

    private static string[] TemporaryFiles(string root)
        => [.. Directory.GetFiles(root, ClusterProfileStore.ProfileName + ".tmp-*", SearchOption.TopDirectoryOnly),
            .. Directory.GetFiles(root, ClusterProfileStore.LegacyBackupName + ".tmp-*", SearchOption.TopDirectoryOnly)];

    private static bool BytesEqual(byte[] actual, byte[] expected) => actual.AsSpan().SequenceEqual(expected);

    private static UnixFileMode? ReadMode(string path)
        => OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(path);

    private static async Task AssertPrivateModeAsync(string path, UnixFileMode expected)
    {
        if (!OperatingSystem.IsWindows())
        { await Assert.That(File.GetUnixFileMode(path)).IsEqualTo(expected); }
    }

    private static async Task AssertModePreservedAsync(string path, UnixFileMode? expected)
    {
        if (!OperatingSystem.IsWindows() && expected is { } mode)
        { await Assert.That(File.GetUnixFileMode(path)).IsEqualTo(mode); }
    }

    private sealed class ProfileFixture : IDisposable
    {
        private ProfileFixture(string root) => Root = root;

        internal string Root { get; }
        internal string ProfilePath => Path.Combine(Root, ClusterProfileStore.ProfileName);

        internal static ProfileFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "keyload-physical-shard-profile-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            if (!OperatingSystem.IsWindows())
            { File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
            return new ProfileFixture(root);
        }

        internal async Task<byte[]> WriteLegacyAsync(UnixFileMode? mode = null)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new LegacyProfile(Incarnation, SigningKey, PeerSecret, AdminKey));
            await File.WriteAllBytesAsync(ProfilePath, bytes);
            if (!OperatingSystem.IsWindows())
            { File.SetUnixFileMode(ProfilePath, mode ?? PrivateFile); }
            return bytes;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private sealed record LegacyProfile(Guid Incarnation, string SigningKey, string PeerSecret, string AdminKey);
    }
}
