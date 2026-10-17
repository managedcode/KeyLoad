using KeyLoad.Storage.IO;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class CurrentStorageSnapshot
{
    private const string OwnerFileName = "owner.lock";
    private const long EmptyOwnerFileLength = 0;
    private const FileAttributes NoUnsupportedAttributes = 0;
    private static readonly int FileBufferBytes = UnitExecutionOptions.StorageExecution().Value.FileBufferBytes;

    internal static async Task<Dictionary<string, byte[]?>> CaptureAsync(string directory)
    {
        var result = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories))
        {
            result.Add(Path.GetRelativePath(directory, path), null);
        }
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(directory, path);
            result.Add(relative, relative == OwnerFileName
                ? await CaptureOwnedEmptyFileAsync(path)
                : await ReadActiveFileAsync(path));
        }
        return result;
    }

    private static async Task<byte[]> ReadActiveFileAsync(string path)
    {
        await using var reader = OfflineRegularFile.OpenReadOnlyObservation(path, FileBufferBytes);
        var bytes = new byte[checked((int)reader.Length)];
        await reader.ReadExactlyAsync(bytes);
        return bytes;
    }

    private static async Task<byte[]> CaptureOwnedEmptyFileAsync(string path)
    {
        var file = new FileInfo(path);
        await Assert.That(file.Length).IsEqualTo(EmptyOwnerFileLength);
        await Assert.That(file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint))
            .IsEqualTo(NoUnsupportedAttributes);
        Assert.ThrowsExactly<IOException>(() =>
        {
            using var competingOwner = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        });
        return [];
    }

    internal static async Task AssertUnchangedAsync(string directory, Dictionary<string, byte[]?> expected)
    {
        var actual = await CaptureAsync(directory);
        await Assert.That(actual.Keys).IsEquivalentTo(expected.Keys);
        foreach (var (path, bytes) in expected)
        {
            if (bytes is null)
            {
                await Assert.That(actual[path]).IsNull();
            }
            else
            {
                await Assert.That(actual[path]!).IsEquivalentTo(bytes, CollectionOrdering.Matching);
            }
        }
    }
}
