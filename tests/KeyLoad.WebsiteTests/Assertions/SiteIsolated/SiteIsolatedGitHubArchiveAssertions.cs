using System.Text.Json.Nodes;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal static class SiteIsolatedGitHubArchiveAssertions
{
    public static void RequireAvailableDisk(JsonObject metadata, string parent)
    {
        var required = checked(metadata[SiteIsolatedGitHubTokens.InputFiles]!.AsArray()
            .Sum(file => file![SiteIsolatedGitHubTokens.Bytes]!.GetValue<long>()) +
            metadata[SiteIsolatedGitHubTokens.WorkerArchives]!.AsArray()
            .Sum(file => file![SiteIsolatedGitHubTokens.Bytes]!.GetValue<long>()) +
            SiteIsolatedGitHubTokens.DiskReserveBytes);
        var drive = DriveInfo.GetDrives().Where(value => parent.StartsWith(
                value.Name.EndsWith(Path.DirectorySeparatorChar) ? value.Name : value.Name + Path.DirectorySeparatorChar,
                StringComparison.Ordinal)).OrderByDescending(value => value.Name.Length).FirstOrDefault();
        if (drive is null || drive.AvailableFreeSpace < required)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Disk);
        }
    }

    public static async Task VerifyMetadataAsync(JsonObject original, string capture, CancellationToken token)
    {
        var actual = JsonNode.Parse(await File.ReadAllBytesAsync(
            Path.Combine(capture, SiteIsolatedGitHubTokens.Metadata), token))!.AsObject();
        if (actual[SiteIsolatedFields.State]!.GetValue<string>() == "selection_verified")
        {
            await Assert.That(JsonNode.DeepEquals(original[SiteIsolatedFields.Cohort], actual[SiteIsolatedFields.Cohort])).IsTrue();
            await Assert.That(JsonNode.DeepEquals(original[SiteIsolatedFields.Source], actual[SiteIsolatedFields.Source])).IsTrue();
            await Assert.That(actual[SiteIsolatedFields.Workers]!.AsArray().Count).IsEqualTo(original[SiteIsolatedFields.Workers]!.AsArray().Count);
            return;
        }

        string[] fields =
        [
            SiteIsolatedGitHubTokens.Source, SiteIsolatedGitHubFields.Repository, SiteIsolatedGitHubFields.WorkflowKey,
            SiteIsolatedGitHubTokens.Run, SiteIsolatedGitHubTokens.Cohort,
            SiteIsolatedGitHubTokens.Workers, SiteIsolatedGitHubFields.Preparation,
            SiteIsolatedFields.RuntimeIdentity,
        ];
        foreach (var field in fields)
        {
            await Assert.That(JsonNode.DeepEquals(original[field], actual[field])).IsTrue();
        }

    }

    public static async Task VerifyAsync(JsonObject original, string capture, CancellationToken token)
    {
        await VerifyMetadataAsync(original, capture, token);
        await SiteIsolatedGitHubArchiveReader.VerifyArchiveHashesAsync(capture, original, token);
    }
}
