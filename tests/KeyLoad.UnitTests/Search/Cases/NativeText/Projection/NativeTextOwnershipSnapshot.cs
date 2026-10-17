using System.Security.Cryptography;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextOwnershipSnapshot
{
    internal static async Task<NativeTextOwnershipSnapshotEntry[]> CaptureAsync(string root,
        CancellationToken cancellationToken)
    {
        var entries = new List<NativeTextOwnershipSnapshotEntry>();
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, path);
            var attributes = File.GetAttributes(path);
            var isDirectory = (attributes & FileAttributes.Directory) != 0;
            if (isDirectory)
            {
                entries.Add(new(relative, true, null));
                continue;
            }
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4_096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var digest = await SHA256.HashDataAsync(input, cancellationToken);
            entries.Add(new(relative, false, Convert.ToHexString(digest)));
        }
        return [.. entries];
    }

    internal static async Task AssertUnchangedAsync(string root, NativeTextOwnershipSnapshotEntry[] expected,
        CancellationToken cancellationToken)
    {
        var actual = await CaptureAsync(root, cancellationToken);
        await Assert.That(actual).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }
}
