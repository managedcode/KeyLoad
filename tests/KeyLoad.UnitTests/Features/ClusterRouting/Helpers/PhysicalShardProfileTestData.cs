using System.Text;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PhysicalShardProfileTestData
{
    internal static byte[][] InvalidProfiles()
    {
        const string valid = "{\"Version\":2,\"PhysicalShardId\":\"0123456789abcdef0123456789abcdef\",\"Incarnation\":\"5cb3e3f8-7d6f-4fb4-b0f5-801c93f3f5bb\",\"SigningKey\":\"AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=\",\"PeerSecret\":\"ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8=\",\"AdminKey\":\"root.0123456789abcdef0123456789abcdef\"}";
        return
        [
            Encoding.UTF8.GetBytes("{"),
            Encoding.UTF8.GetBytes(valid.Replace("\"Version\":2,", "\"Version\":2,\"Version\":2,", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(valid.Replace("\"PhysicalShardId\":\"0123456789abcdef0123456789abcdef\",", "", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(valid[..^1] + ",\"Unknown\":1}"),
            Encoding.UTF8.GetBytes(valid.Replace("\"Version\":2", "\"Version\":1", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(valid.Replace("0123456789abcdef0123456789abcdef", "00000000000000000000000000000000", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(new string(' ', 8193))
        ];
    }

    internal static void AssertOpenAndUpgradeRejected(string root)
    {
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => ClusterProfileStore.Open(root, UnitProfileOptions.Execution()));
        AssertOfflineUpgradeRejected(root);
    }

    internal static void AssertOfflineUpgradeRejected(string root)
        => _ = Assert.ThrowsExactly<InvalidOperationException>(() => ClusterProfileStore.UpgradeLegacyOffline(root, UnitProfileOptions.Execution()));

    internal static void SetPrivateFileMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    }
}
