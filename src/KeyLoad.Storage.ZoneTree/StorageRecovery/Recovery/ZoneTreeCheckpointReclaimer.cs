using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeCheckpointReclaimer
{
    private const int TemporarySeparatorWidth = 1;
    private const int NoFileAttributes = 0;

    internal static void Reclaim(string directory)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            var name = Path.GetFileName(path);
            var retired = name.StartsWith(RetiredTreePrefix, StringComparison.Ordinal)
                && Guid.TryParseExact(name[RetiredTreePrefix.Length..], GuidFormat, out _);
            var temporary = (name.StartsWith(CheckpointTemporaryPrefix, StringComparison.Ordinal)
                || name.StartsWith(InstallTemporaryPrefix, StringComparison.Ordinal))
                && name.EndsWith(TemporaryFileSuffix, StringComparison.Ordinal)
                && Guid.TryParseExact(name[(name.IndexOf(TemporaryNameSeparator, StringComparison.Ordinal) + TemporarySeparatorWidth)..^TemporaryFileSuffix.Length],
                    GuidFormat, out _);
            if (!retired && !temporary)
            {
                continue;
            }

            DeleteCandidate(path, retired);
        }
    }

    private static void DeleteCandidate(string path, bool retired)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != NoFileAttributes)
            {
                return;
            }

            if (retired)
            {
                Directory.Delete(path, true);
            }
            else
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // The verified canonical journal is open; locked derived files wait for the next reclamation.
        }
    }
}
