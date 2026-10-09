using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionPairedIdentityInventory
{
    internal static async Task<PartitionPairedIdentityInventoryCut> CaptureAsync(string root)
    {
        var bytes = await RuntimeJournalReaderFixture.ReadInventoryAsync(root).ConfigureAwait(false);
        var modes = new Dictionary<string, UnixFileMode>(StringComparer.Ordinal);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            modes.Add(string.Empty, File.GetUnixFileMode(root));
            foreach (var path in bytes.Keys)
            {
                modes.Add(path, File.GetUnixFileMode(Path.Combine(root, path)));
            }
        }
        return new(bytes, modes);
    }

    internal static async Task RequireUnchangedAsync(string root, PartitionPairedIdentityInventoryCut expected)
    {
        var actual = await CaptureAsync(root).ConfigureAwait(false);
        await Assert.That(actual.Bytes.Keys.Order(StringComparer.Ordinal))
            .IsEquivalentTo(expected.Bytes.Keys.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
        foreach (var (path, bytes) in expected.Bytes)
        {
            if (bytes is null)
            {
                await Assert.That(actual.Bytes[path]).IsNull();
            }
            else
            {
                await Assert.That(actual.Bytes[path]!).IsEquivalentTo(bytes, CollectionOrdering.Matching);
            }
        }
        await Assert.That(actual.Modes.Keys.Order(StringComparer.Ordinal))
            .IsEquivalentTo(expected.Modes.Keys.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
        foreach (var (path, mode) in expected.Modes)
        {
            await Assert.That(actual.Modes[path]).IsEqualTo(mode);
        }
    }
}
