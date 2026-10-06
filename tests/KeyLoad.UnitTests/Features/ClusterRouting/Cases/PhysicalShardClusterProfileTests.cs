using System.Text.Json;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PhysicalShardClusterProfileTests
{
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

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
    public async Task CurrentProfileSymlinkIsRejectedWithoutChangingSource()
    {
        if (OperatingSystem.IsWindows())
        { return; }
        using var fixture = ProfileFixture.Create();
        var outside = Path.Combine(fixture.Root, "outside-profile.json");
        var original = await fixture.WriteCurrentAsync();
        File.Move(fixture.ProfilePath, outside);
        File.CreateSymbolicLink(fixture.ProfilePath, outside);
        PhysicalShardProfileTestData.AssertOpenRejected(fixture.Root);
        await Assert.That(File.Exists(fixture.ProfilePath)).IsTrue();
        await Assert.That(BytesEqual(await File.ReadAllBytesAsync(outside), original)).IsTrue();
    }

    [Test]
    public async Task CurrentProfileWithUnsafePermissionsIsRejectedWithoutRewrite()
    {
        if (OperatingSystem.IsWindows())
        { return; }
        foreach (var mode in new[] { UnixFileMode.UserRead | UnixFileMode.GroupRead,
            UnixFileMode.UserRead | UnixFileMode.OtherRead, UnixFileMode.UserWrite })
        {
            using var fixture = ProfileFixture.Create();
            var original = await fixture.WriteCurrentAsync(mode);
            PhysicalShardProfileTestData.AssertOpenRejected(fixture.Root);
            await Assert.That(File.GetUnixFileMode(fixture.ProfilePath)).IsEqualTo(mode);
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
        => Directory.GetFiles(root, ClusterProfileStore.ProfileName + ".tmp-*", SearchOption.TopDirectoryOnly);

    private static bool BytesEqual(byte[] actual, byte[] expected) => actual.AsSpan().SequenceEqual(expected);

    private static async Task AssertPrivateModeAsync(string path, UnixFileMode expected)
    {
        if (!OperatingSystem.IsWindows())
        { await Assert.That(File.GetUnixFileMode(path)).IsEqualTo(expected); }
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

        internal async Task<byte[]> WriteCurrentAsync(UnixFileMode? mode = null)
        {
            _ = ClusterProfileStore.Open(Root, UnitProfileOptions.Execution());
            var bytes = await File.ReadAllBytesAsync(ProfilePath);
            if (!OperatingSystem.IsWindows())
            { File.SetUnixFileMode(ProfilePath, mode ?? PrivateFile); }
            return bytes;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);

    }
}
