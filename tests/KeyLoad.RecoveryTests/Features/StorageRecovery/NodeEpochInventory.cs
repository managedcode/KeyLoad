namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed record NodeEpochInventory(string[] Directories, Dictionary<string, string> Files);

internal static class NodeEpochInventoryCapture
{
    private const string SearchPattern = "*";

    internal static async Task<NodeEpochInventory> CaptureAsync(string root, CancellationToken cancellationToken)
    {
        var directories = Directory.EnumerateDirectories(root, SearchPattern, SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path)).Order(StringComparer.Ordinal).ToArray();
        var files = await EpochUpgradeFileInventory.CaptureAsync(root, cancellationToken);
        return new(directories, files);
    }

    internal static async Task AssertUnchangedAsync(string root, NodeEpochInventory expected,
        CancellationToken cancellationToken)
    {
        var actual = await CaptureAsync(root, cancellationToken);
        await Assert.That(actual.Directories).IsEquivalentTo(expected.Directories);
        await EpochUpgradeFileInventory.AssertUnchangedAsync(root, expected.Files, cancellationToken);
    }
}
