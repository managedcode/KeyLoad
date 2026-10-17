using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NativeJournalRefusalFiles(ZoneTreeExistingStoreFixture files)
{
    private const string HiddenSuffix = ".original";
    internal bool IsHidden
    {
        get; private set;
    }
    private string HiddenPath => files.JournalPath + HiddenSuffix;

    internal void Change(NativeJournalEntryKind kind)
    {
        files.HideFile(ZoneTreeExistingStoreFixture.JournalFile);
        IsHidden = true;
        if (kind == NativeJournalEntryKind.Symlink)
        {
            File.CreateSymbolicLink(files.JournalPath, HiddenPath);
        }
        else if (kind == NativeJournalEntryKind.Fifo)
        {
            RequestCqrsProbeNativeFifo.Create(files.JournalPath);
        }
    }

    internal void Restore()
    {
        File.Delete(files.JournalPath);
        File.Move(HiddenPath, files.JournalPath);
        IsHidden = false;
    }

    internal async Task RequireOriginalsAsync(IReadOnlyDictionary<string, byte[]?> expected,
        NativeJournalEntryKind kind)
    {
        var expectedNames = expected.Keys.Append(ZoneTreeExistingStoreFixture.JournalFile + HiddenSuffix).ToList();
        if (kind == NativeJournalEntryKind.Missing)
        {
            expectedNames.Remove(ZoneTreeExistingStoreFixture.JournalFile);
        }
        var names = Directory.EnumerateFileSystemEntries(files.DirectoryPath, SearchPattern, SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(files.DirectoryPath, path)).Order(StringComparer.Ordinal).ToArray();
        await Assert.That(names).IsEquivalentTo(expectedNames.Order(StringComparer.Ordinal),
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        foreach (var pair in expected)
        {
            var path = pair.Key == ZoneTreeExistingStoreFixture.JournalFile
                ? HiddenPath : Path.Combine(files.DirectoryPath, pair.Key);
            if (pair.Value is null)
            {
                await Assert.That(Directory.Exists(path)).IsTrue();
            }
            else
            {
                var actual = await File.ReadAllBytesAsync(path);
                await Assert.That(actual.AsSpan().SequenceEqual(pair.Value)).IsTrue();
            }
        }
    }

    private const string SearchPattern = "*";
}
