using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class EpochStorageSnapshot
{
    internal static async Task<Dictionary<string, byte[]?>> CaptureAsync(string directory)
    {
        var result = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories))
        {
            result.Add(Path.GetRelativePath(directory, path), null);
        }
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            result.Add(Path.GetRelativePath(directory, path), await File.ReadAllBytesAsync(path));
        }
        return result;
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
