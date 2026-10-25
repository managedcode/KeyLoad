namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class PartitionRootDirectoryOperations
{
    private const string SentinelName = "sentinel";
    internal static void Create(string path, string target)
    {
        _ = target;
        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path, SentinelName), [0x51, 0x00, 0x52]);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(Path.Combine(path, SentinelName), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    internal static async Task VerifyAsync(string path, string target)
    {
        _ = target;
        await Assert.That(await File.ReadAllBytesAsync(Path.Combine(path, SentinelName)))
            .IsEquivalentTo(new byte[] { 0x51, 0x00, 0x52 });
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            await Assert.That(File.GetUnixFileMode(path)).IsEqualTo(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await Assert.That(File.GetUnixFileMode(Path.Combine(path, SentinelName)))
                .IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

}
