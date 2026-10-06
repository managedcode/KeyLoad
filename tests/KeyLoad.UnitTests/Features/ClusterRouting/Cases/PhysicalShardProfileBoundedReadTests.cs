using System.Text.Json;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PhysicalShardProfileBoundedReadTests
{
    private const int MaximumProfileBytes = 8192;

    [Test]
    public async Task NativeConfiguredByteCapControlsRealReopenWithoutChangingTheProfile()
    {
        using var fixture = ProfileFixture.Create();
        using var configuration = new ConfigurationManager();
        configuration["KeyLoad:ClusterProfileExecution:MaximumProfileBytes"] = "512";
        var options = AppHostOptionsRegistration.BindProfileExecution(configuration);
        var profile = CreateProfile();
        var bytes = PadProfile(profile, 512);
        fixture.Write(bytes);
        await Assert.That(ClusterProfileStore.Open(fixture.Root, options)).IsEqualTo(profile);
        fixture.Write(PadProfile(profile, 513));
        Assert.ThrowsExactly<InvalidOperationException>(() => ClusterProfileStore.Open(fixture.Root, options));
        await Assert.That((await File.ReadAllBytesAsync(fixture.ProfilePath)).Length).IsEqualTo(513);
    }

    [Test]
    public async Task ConfiguredWriterRejectsOversizedProfileBeforeCreatingTheFile()
    {
        using var fixture = ProfileFixture.Create();
        using var configuration = new ConfigurationManager();
        configuration["KeyLoad:ClusterProfileExecution:MaximumProfileBytes"] = "64";
        var options = AppHostOptionsRegistration.BindProfileExecution(configuration);
        Assert.ThrowsExactly<InvalidOperationException>(() => ClusterProfileStore.WriteStage(
            fixture.ProfilePath, new byte[65], null, options));
        await Assert.That(File.Exists(fixture.ProfilePath)).IsFalse();
    }

    [Test]
    public async Task NativeProfileAtExactByteCapOpensWithoutChangingItsBytes()
    {
        using var fixture = ProfileFixture.Create();
        var profile = CreateProfile();
        var bytes = PadProfile(profile, MaximumProfileBytes);
        fixture.Write(bytes);

        var reopened = ClusterProfileStore.Open(fixture.Root, UnitProfileOptions.Execution());

        await Assert.That(reopened).IsEqualTo(profile);
        await Assert.That((await File.ReadAllBytesAsync(fixture.ProfilePath)).AsSpan().SequenceEqual(bytes)).IsTrue();
    }

    [Test]
    public async Task NativeProfileOneByteOverCapIsRejectedWithoutChangingItsBytes()
    {
        using var fixture = ProfileFixture.Create();
        var bytes = PadProfile(CreateProfile(), MaximumProfileBytes + 1);
        fixture.Write(bytes);

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => ClusterProfileStore.Open(fixture.Root, UnitProfileOptions.Execution()));

        await Assert.That(error.Message).IsEqualTo(ClusterProfileStore.InvalidProfile);
        await Assert.That((await File.ReadAllBytesAsync(fixture.ProfilePath)).AsSpan().SequenceEqual(bytes)).IsTrue();
    }

    private static LocalProfile CreateProfile()
        => new(2, Guid.NewGuid(), Guid.NewGuid(), Secret(), Secret(), "root." + Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()));

    private static string Secret() => Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());

    private static byte[] PadProfile(LocalProfile profile, int size)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(profile, ClusterProfileStore.CreateJson(UnitProfileOptions.Execution()));
        if (json.Length >= size)
        { throw new InvalidOperationException("The bounded profile fixture must have room for JSON whitespace."); }
        var bytes = new byte[size];
        json.CopyTo(bytes, 0);
        bytes.AsSpan(json.Length).Fill((byte)' ');
        return bytes;
    }

    private sealed class ProfileFixture : IDisposable
    {
        private ProfileFixture(string root) => Root = root;

        internal string Root { get; }
        internal string ProfilePath => Path.Combine(Root, ClusterProfileStore.ProfileName);

        internal static ProfileFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "keyload-physical-shard-bounded-profile-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            if (!OperatingSystem.IsWindows())
            { File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
            return new ProfileFixture(root);
        }

        internal void Write(byte[] bytes)
        {
            File.WriteAllBytes(ProfilePath, bytes);
            if (!OperatingSystem.IsWindows())
            { File.SetUnixFileMode(ProfilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
