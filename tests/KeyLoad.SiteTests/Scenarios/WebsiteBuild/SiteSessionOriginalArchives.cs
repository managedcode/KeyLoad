using System.IO.Compression;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>Checks suite bytes against the same-run original worker ZIPs before any extracted output exists.</summary>
internal static class SiteSessionOriginalArchives
{
    private const string ExpectedFiles = "suiteFiles";
    private const string OriginalArchives = "workerArchives";
    private const string WorkerPrefix = "workers/";
    private const string Separator = "/";
    private const long ArchiveBytes = 536_870_912;
    private const long MaximumOriginalBytes = 1_073_741_824;

    internal static async Task VerifyAsync(string capture, JsonObject metadata,
        SiteIsolatedGitHubArchiveEntry[] suite, CancellationToken token)
    {
        var expected = metadata[ExpectedFiles]!.AsArray().ToDictionary(
            item => item![SiteIsolatedGitHubTokens.Path]!.GetValue<string>(),
            item => item![SiteIsolatedGitHubTokens.Sha256]!.GetValue<string>(), StringComparer.Ordinal);
        foreach (var file in suite)
        {
            if (!expected.TryGetValue(file.File.Path, out var hash) || hash != file.File.Sha256)
            {
                throw new InvalidDataException(SiteIsolatedGitHubTokens.Changed);
            }
        }

        foreach (var selected in metadata[OriginalArchives]!.AsArray())
        {
            var archive = selected!.AsObject();
            var id = archive[SiteIsolatedGitHubTokens.Id]!.GetValue<string>();
            var relative = archive[SiteIsolatedGitHubTokens.Path]!.GetValue<string>();
            var target = SiteIsolatedGitHubFileOperations.FixedTarget(capture, relative);
            var hash = await SiteIsolatedGitHubFileOperations.HashAsync(target, relative, ArchiveBytes, token);
            if (hash.Sha256 != archive[SiteIsolatedGitHubTokens.Sha256]!.GetValue<string>()
                || hash.Bytes != archive[SiteIsolatedGitHubTokens.Bytes]!.GetValue<long>())
            {
                throw new InvalidDataException(SiteIsolatedGitHubTokens.Changed);
            }

            var prefix = WorkerPrefix + id + Separator;
            var references = expected.Where(item => item.Key.StartsWith(prefix, StringComparison.Ordinal))
                .ToDictionary(item => item.Key[prefix.Length..], item => item.Value, StringComparer.Ordinal);
            await VerifyWorkerAsync(target, references, token);
        }
    }

    private static async Task VerifyWorkerAsync(string target, Dictionary<string, string> expected, CancellationToken token)
    {
        await using var source = File.OpenRead(target);
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            SiteIsolatedGitHubArchivePreflight.ValidatePath(entry.FullName, seen);
            var directory = entry.FullName.EndsWith(Separator, StringComparison.Ordinal);
            SiteIsolatedGitHubArchivePreflight.ValidateType(entry, directory);
            total = checked(total + entry.Length);
            if (seen.Count > SiteIsolatedGitHubTokens.MaximumEntries || entry.Length < 0
                || entry.Length > SiteIsolatedGitHubTokens.RawBytes || total > MaximumOriginalBytes)
            {
                throw new InvalidDataException(SiteIsolatedGitHubTokens.Bounds);
            }

            if (directory)
            {
                if (entry.Length != 0)
                {
                    throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidPath);
                }

                continue;
            }

            var hash = await SiteIsolatedGitHubEntryOperations.HashEntryAsync(entry, SiteIsolatedGitHubTokens.RawBytes, token);
            if (expected.TryGetValue(entry.FullName, out var expectedHash))
            {
                if (hash.Sha256 != expectedHash)
                {
                    throw new InvalidDataException(SiteIsolatedGitHubTokens.Changed);
                }

                found.Add(entry.FullName);
            }
        }

        if (found.Count != expected.Count)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Incomplete);
        }
    }
}
