using System.Text.Json.Nodes;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal static class SiteIsolatedGitHubArchiveReader
{
    internal const string PreparedInput = "prepared-input";
    private const string AggregatePrefix = "input/aggregate/";
    private const string ProviderPrefix = "input/provider/";

    public static async Task<SiteIsolatedGitHubArchiveReceipt> ExtractAsync(string capture,
        string receiptPath, JsonObject metadata, CancellationToken token)
    {
        var files = ReadFiles(metadata);
        await VerifyPreparedAsync(capture, files, token);
        await VerifyOriginalsAsync(capture, metadata, files, token);
        return await SiteIsolatedGitHubArchivePublication.PublishAsync(capture, receiptPath, metadata, files, token);
    }

    internal static SiteIsolatedGitHubArchiveFile[] ReadFiles(JsonObject metadata)
        => metadata[SiteIsolatedGitHubTokens.InputFiles]!.AsArray().Select(file => new SiteIsolatedGitHubArchiveFile(
            file![SiteIsolatedGitHubTokens.Path]!.GetValue<string>(),
            file[SiteIsolatedGitHubTokens.Bytes]!.GetValue<long>(),
            file[SiteIsolatedGitHubTokens.Sha256]!.GetValue<string>())).ToArray();

    internal static async Task VerifyOriginalsAsync(string capture, JsonObject metadata,
        SiteIsolatedGitHubArchiveFile[] files, CancellationToken token)
    {
        var suite = files.Where(file => file.Path.StartsWith(AggregatePrefix, StringComparison.Ordinal))
            .Select(file => file with { Path = file.Path[AggregatePrefix.Length..] }).ToArray();
        var provider = files.Where(file => file.Path.StartsWith(ProviderPrefix, StringComparison.Ordinal))
            .Select(file => file with { Path = file.Path[ProviderPrefix.Length..] }).ToArray();
        await SiteSessionOriginalArchives.VerifyAsync(capture, metadata, suite, token);
        await SiteSessionPreparationArchives.VerifyAsync(capture, metadata, provider, token);
    }

    internal static async Task VerifyArchiveHashesAsync(string capture, JsonObject metadata, CancellationToken token)
    {
        var archives = metadata[SiteIsolatedGitHubTokens.WorkerArchives]!.AsArray()
            .Append(metadata[SiteIsolatedFields.RuntimeIdentity]![SiteIsolatedFields.Archive]);
        foreach (var archive in archives)
        {
            var relative = archive![SiteIsolatedGitHubTokens.Path]!.GetValue<string>();
            var expected = new SiteIsolatedGitHubArchiveFile(relative,
                archive[SiteIsolatedGitHubTokens.Bytes]!.GetValue<long>(),
                archive[SiteIsolatedGitHubTokens.Sha256]!.GetValue<string>());
            var actual = await SiteIsolatedGitHubFileOperations.HashAsync(
                SiteIsolatedGitHubFileOperations.FixedTarget(capture, relative), relative, expected.Bytes, token);
            if (actual != expected)
            {
                throw new InvalidDataException(SiteIsolatedGitHubTokens.Changed);
            }
        }
    }

    internal static async Task VerifyPreparedAsync(string capture, SiteIsolatedGitHubArchiveFile[] files,
        CancellationToken token)
    {
        if (files.Sum(file => file.Bytes) > SiteIsolatedGitHubTokens.TotalRawBytes)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Bounds);
        }

        var prepared = SiteIsolatedGitHubFileOperations.FixedTarget(capture, PreparedInput);
        var actualPaths = new HashSet<string>(StringComparer.Ordinal);
        ReadPaths(prepared, prepared, actualPaths, 0);
        if (actualPaths.Count != files.Length)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Incomplete);
        }

        foreach (var file in files)
        {
            if (!file.Path.StartsWith(SiteIsolatedGitHubTokens.Input + SiteIsolatedGitHubTokens.Slash, StringComparison.Ordinal))
            {
                throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidPath);
            }

            var relative = file.Path[(SiteIsolatedGitHubTokens.Input.Length + 1)..];
            if (!actualPaths.Remove(relative))
            {
                throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidPath);
            }

            var actual = await SiteIsolatedGitHubFileOperations.HashAsync(
                SiteIsolatedGitHubFileOperations.FixedTarget(prepared, relative), file.Path, file.Bytes, token);
            if (actual != file)
            {
                throw new InvalidDataException(SiteIsolatedGitHubTokens.Changed);
            }
        }
    }

    private static void ReadPaths(string root, string directory, HashSet<string> files, int depth)
    {
        var info = new DirectoryInfo(directory);
        if (!info.Exists || info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0 || depth > 8)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidPath);
        }

        foreach (var entry in info.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo)
            {
                ReadPaths(root, entry.FullName, files, depth + 1);
            }
            else
            {
                SiteIsolatedGitHubFileOperations.RequireRegular(entry.FullName);
                files.Add(Path.GetRelativePath(root, entry.FullName).Replace(Path.DirectorySeparatorChar, '/'));
            }

            if (files.Count > SiteIsolatedGitHubTokens.MaximumEntries)
            {
                throw new InvalidDataException(SiteIsolatedGitHubTokens.Bounds);
            }
        }
    }
}
