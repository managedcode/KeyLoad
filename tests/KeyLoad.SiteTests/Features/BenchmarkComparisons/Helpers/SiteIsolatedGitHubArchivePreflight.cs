using System.IO.Compression;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteIsolatedGitHubArchiveEntry(ZipArchiveEntry Entry, SiteIsolatedGitHubArchiveFile File);

internal static class SiteIsolatedGitHubArchivePreflight
{
    public static async Task<SiteIsolatedGitHubArchiveEntry[]> InspectAsync(ZipArchive archive,
        HashSet<string> expected, bool provider, CancellationToken token, IReadOnlySet<string>? cellIds = null)
    {
        var directories = SiteIsolatedGitHubArchivePaths.ParentDirectories(expected);
        if (provider)
        {
            directories.Add(SiteIsolatedGitHubFields.RequestsDirectory + SiteIsolatedGitHubTokens.Slash);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selected = new List<SiteIsolatedGitHubArchiveEntry>();
        long total = SiteIsolatedGitHubTokens.Zero;
        foreach (var entry in archive.Entries)
        {
            ValidatePath(entry.FullName, seen);
            if (seen.Count > SiteIsolatedGitHubTokens.MaximumEntries)
            {
                throw new InvalidDataException(SiteIsolatedGitHubTokens.Bounds);
            }

            var directory = entry.FullName.EndsWith(SiteIsolatedGitHubTokens.Slash, StringComparison.Ordinal);
            ValidateType(entry, directory);
            ValidateExpected(entry, expected, directories, provider, directory, cellIds);
            var maximum = Maximum(entry.FullName, provider, directory);
            if (entry.Length < SiteIsolatedGitHubTokens.Zero || entry.Length > maximum)
            {
                throw new InvalidDataException(SiteIsolatedGitHubTokens.Bounds);
            }

            total = checked(total + entry.Length);
            ValidateTotal(total, provider);
            var digest = await SiteIsolatedGitHubEntryOperations.HashEntryAsync(entry, maximum, token);
            if (expected.Contains(entry.FullName))
            {
                selected.Add(new(entry, digest));
            }
        }

        if (selected.Count != expected.Count)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Incomplete);
        }

        RequireRawTotal(selected);

        return [.. selected];
    }

    private static void ValidatePath(string path, HashSet<string> seen)
    {
        if (string.IsNullOrEmpty(path) || path.Contains(SiteIsolatedGitHubTokens.Backslash, StringComparison.Ordinal) ||
            Path.IsPathRooted(path) || path.Split(SiteIsolatedGitHubTokens.Slash).Any(part =>
                part is SiteIsolatedGitHubTokens.Dot or SiteIsolatedGitHubTokens.Parent))
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidPath);
        }

        if (!seen.Add(path))
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Duplicate);
        }
    }

    private static void ValidateType(ZipArchiveEntry entry, bool directory)
    {
        var unix = (entry.ExternalAttributes >> SiteIsolatedGitHubTokens.UnixShift) & SiteIsolatedGitHubTokens.UnixMask;
        var allowed = directory ? SiteIsolatedGitHubTokens.UnixDirectory : SiteIsolatedGitHubTokens.UnixRegular;
        var attributes = (FileAttributes)entry.ExternalAttributes;
        if ((unix != SiteIsolatedGitHubTokens.Zero && unix != allowed) ||
            (attributes & FileAttributes.ReparsePoint) != SiteIsolatedGitHubTokens.Zero ||
            (!directory && (attributes & FileAttributes.Directory) != SiteIsolatedGitHubTokens.Zero))
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidPath);
        }
    }

    private static void ValidateExpected(ZipArchiveEntry entry, HashSet<string> expected,
        HashSet<string> directories, bool provider, bool directory, IReadOnlySet<string>? cellIds)
    {
        if (directory)
        {
            if (!directories.Contains(entry.FullName) || entry.Length != SiteIsolatedGitHubTokens.Zero)
            {
                throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidPath);
            }

            return;
        }

        if (!expected.Contains(entry.FullName) && (!provider || !SiteIsolatedGitHubArchivePaths.IsProviderCapture(entry.FullName, cellIds)))
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidPath);
        }
    }

    private static long Maximum(string name, bool provider, bool directory)
    {
        if (directory)
        {
            return SiteIsolatedGitHubTokens.Zero;
        }

        if (name.EndsWith(SiteIsolatedGitHubTokens.Slash + SiteIsolatedGitHubTokens.Raw, StringComparison.Ordinal))
        {
            return SiteIsolatedGitHubTokens.RawBytes;
        }

        return provider && !SiteIsolatedGitHubArchivePaths.ExpectedProvider().Contains(name)
            ? SiteIsolatedGitHubTokens.MetadataBytes : SiteIsolatedGitHubTokens.JsonBytes;
    }

    private static void ValidateTotal(long total, bool provider)
    {
        var maximum = provider ? SiteIsolatedGitHubTokens.ProviderBytes :
            SiteIsolatedGitHubTokens.TotalRawBytes + SiteIsolatedGitHubTokens.JsonBytes;
        if (total > maximum)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Bounds);
        }
    }

    private static void RequireRawTotal(IEnumerable<SiteIsolatedGitHubArchiveEntry> selected)
    {
        var total = selected.Where(entry => entry.File.Path.EndsWith(
            SiteIsolatedGitHubTokens.Slash + SiteIsolatedGitHubTokens.Raw, StringComparison.Ordinal))
            .Sum(entry => entry.File.Bytes);
        if (total > SiteIsolatedGitHubTokens.TotalRawBytes)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Bounds);
        }
    }
}
