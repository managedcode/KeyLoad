using System.IO.Compression;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.BenchmarkComparisons;

/// <summary>Compares published preparation evidence with each original same-run artifact ZIP.</summary>
internal static class SiteSessionPreparationArchives
{
    private const long SmallArchiveBytes = 134_217_728;
    private const long IdentityFileBytes = 67_108_864;
    private const string Runtime = "github/runtime/";

    internal static async Task VerifyAsync(string capture, JsonObject metadata,
        SiteIsolatedGitHubArchiveEntry[] provider, CancellationToken token)
    {
        var runtime = provider.Where(item => item.File.Path.StartsWith(Runtime, StringComparison.Ordinal))
            .ToDictionary(item => item.File.Path[Runtime.Length..], item => item.File.Sha256, StringComparer.Ordinal);
        await VerifyArchiveAsync(capture, metadata[SiteIsolatedFields.RuntimeIdentity]!.AsObject(), runtime, SmallArchiveBytes, token);
        var proofEntry = provider.Single(item => item.File.Path == "proof.json");
        await using var proofStream = await proofEntry.Entry.OpenAsync(token);
        var proof = (await JsonNode.ParseAsync(proofStream, cancellationToken: token))!.AsObject();
        var functional = proof[SiteIsolatedFields.Preparation]![SiteIsolatedFields.Functional]!;
        var expected = functional[SiteIsolatedFields.Files]!.AsArray().ToDictionary(
            item => item![SiteIsolatedFields.Entry]!.GetValue<string>(), item => item![SiteIsolatedFields.Sha256]!.GetValue<string>(), StringComparer.Ordinal);
        foreach (var file in functional[SiteIsolatedFields.Files]!.AsArray())
        {
            if (provider.Single(item => item.File.Path == file![SiteIsolatedFields.Path]!.GetValue<string>()).File.Sha256
                != file![SiteIsolatedFields.Sha256]!.GetValue<string>())
            {
                throw new InvalidDataException(SiteIsolatedGitHubTokens.Changed);
            }
        }

        await VerifyArchiveAsync(capture, metadata[SiteIsolatedFields.Functional]!.AsObject(), expected, SmallArchiveBytes, token);
    }

    private static async Task VerifyArchiveAsync(string capture, JsonObject selection,
        Dictionary<string, string> expected, long maximumBytes, CancellationToken token)
    {
        var selected = selection[SiteIsolatedFields.Archive]!;
        var relative = selected[SiteIsolatedFields.Path]!.GetValue<string>();
        var target = SiteIsolatedGitHubFileOperations.FixedTarget(capture, relative);
        var actual = await SiteIsolatedGitHubFileOperations.HashAsync(target, relative, maximumBytes, token);
        if (actual.Sha256 != selected[SiteIsolatedFields.Sha256]!.GetValue<string>() || actual.Bytes != selected[SiteIsolatedFields.Bytes]!.GetValue<long>())
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Changed);
        }

        await using var stream = File.OpenRead(target);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            SiteIsolatedGitHubArchivePreflight.ValidatePath(entry.FullName, seen);
            var directory = entry.FullName.EndsWith('/');
            SiteIsolatedGitHubArchivePreflight.ValidateType(entry, directory);
            total = checked(total + entry.Length);
            if (seen.Count > SiteIsolatedGitHubTokens.MaximumEntries || entry.Length < 0
                || total > SmallArchiveBytes || directory && entry.Length != 0)
            {
                throw new InvalidDataException(SiteIsolatedGitHubTokens.Bounds);
            }

            if (expected.TryGetValue(entry.FullName, out var digest))
            {
                var file = await SiteIsolatedGitHubEntryOperations.HashEntryAsync(entry, IdentityFileBytes, token);
                if (file.Sha256 != digest)
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
