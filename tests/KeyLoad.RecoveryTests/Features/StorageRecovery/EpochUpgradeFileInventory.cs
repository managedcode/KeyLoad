using System.Security.Cryptography;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class EpochUpgradeFileInventory
{
    private const string OwnerLockName = "owner.lock";
    private const string JournalName = "commands.wal";
    private const string MetadataWalRelativePath = "tree/0.meta.wal";
    private const string AllFilesPattern = "*";

    internal static async Task<Dictionary<string, string>> CaptureAsync(string directory,
        CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(directory, AllFilesPattern, SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 4_096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var digest = await SHA256.HashDataAsync(input, cancellationToken);
            files.Add(Path.GetRelativePath(directory, path), Convert.ToHexString(digest));
        }
        return files;
    }

    internal static async Task AssertUnchangedAsync(string directory, Dictionary<string, string> expected,
        CancellationToken cancellationToken)
    {
        var actual = await CaptureAsync(directory, cancellationToken);
        await Assert.That(actual.Keys).IsEquivalentTo(expected.Keys);
        foreach (var file in expected)
        {
            await Assert.That(actual[file.Key]).IsEqualTo(file.Value);
        }
    }

    internal static void AssertNativeHandlesReleased(string directory)
    {
        foreach (var file in OwnedFiles(directory))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
    }

    private static IEnumerable<string> OwnedFiles(string directory)
    {
        foreach (var path in Directory.EnumerateFiles(directory, OwnerLockName, SearchOption.AllDirectories))
        {
            yield return path;
        }
        foreach (var path in Directory.EnumerateFiles(directory, JournalName, SearchOption.AllDirectories))
        {
            yield return path;
        }
        foreach (var path in Directory.EnumerateFiles(directory, "0.meta.wal", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetRelativePath(directory, path), MetadataWalRelativePath, StringComparison.Ordinal))
            {
                yield return path;
            }
        }
    }
}
