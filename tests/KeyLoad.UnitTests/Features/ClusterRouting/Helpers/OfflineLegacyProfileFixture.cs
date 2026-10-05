using System.Text.Json;

namespace KeyLoad.UnitTests.Features.ClusterRouting.Helpers;

internal sealed class OfflineLegacyProfileFixture : IDisposable
{
    private const UnixFileMode PrivateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const string SigningKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
    private const string PeerSecret = "ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8=";
    private const string AdminKey = "root.0123456789abcdef0123456789abcdef";
    internal static readonly Guid Incarnation = Guid.Parse("5cb3e3f8-7d6f-4fb4-b0f5-801c93f3f5bb");

    private OfflineLegacyProfileFixture(string root, byte[] original)
    {
        Root = root;
        Original = original;
    }

    internal string Root { get; }
    internal string ProfilePath => Path.Combine(Root, ClusterProfileStore.ProfileName);
    internal string BackupPath => Path.Combine(Root, ClusterProfileStore.LegacyBackupName);
    internal byte[] Original { get; }
    internal const string ExpectedSigningKey = SigningKey;
    internal const string ExpectedPeerSecret = PeerSecret;
    internal const string ExpectedAdminKey = AdminKey;

    internal static async Task<OfflineLegacyProfileFixture> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-profile-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(root, PrivateDirectory); }
        var original = JsonSerializer.SerializeToUtf8Bytes(new LegacyProfile(Incarnation,
            SigningKey, PeerSecret, AdminKey));
        var path = Path.Combine(root, ClusterProfileStore.ProfileName);
        await File.WriteAllBytesAsync(path, original);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(path, PrivateFile); }
        return new(root, original);
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);

    private sealed record LegacyProfile(Guid Incarnation, string SigningKey, string PeerSecret, string AdminKey);
}
