using System.Text;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class SiloProfileTestData
{
    internal static byte[][] InvalidProfiles()
    {
        const string valid = "{\"Version\":2,\"Incarnation\":\"5cb3e3f8-7d6f-4fb4-b0f5-801c93f3f5bb\",\"SigningKey\":\"AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=\",\"AdminKey\":\"root.0123456789abcdef0123456789abcdef\"}";
        return
        [
            Encoding.UTF8.GetBytes("{"),
            Encoding.UTF8.GetBytes(valid.Replace("\"Version\":2,", "\"Version\":2,\"Version\":2,", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(valid.Replace("\"Incarnation\":\"5cb3e3f8-7d6f-4fb4-b0f5-801c93f3f5bb\",", "", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(valid[..^1] + ",\"Unknown\":1}"),
            Encoding.UTF8.GetBytes(valid.Replace("\"Version\":2", "\"Version\":8", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(valid.Replace("5cb3e3f8-7d6f-4fb4-b0f5-801c93f3f5bb", "00000000-0000-0000-0000-000000000000", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(new string(' ', 8193))
        ];
    }

    internal static void AssertOpenRejected(string root)
        => _ = Assert.ThrowsExactly<InvalidOperationException>(() =>
            ClusterProfileStore.Open(root, UnitProfileOptions.Execution()));

    internal static void SetPrivateFileMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
