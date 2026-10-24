using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.BenchmarkComparisons;

internal static class SiteIsolatedGitHubArchiveAssertions
{
    public static void RequireAvailableDisk(JsonObject metadata, string parent)
    {
        var artifacts = metadata[SiteIsolatedGitHubTokens.Artifacts]!;
        var required = checked(artifacts[SiteIsolatedGitHubTokens.SuiteKey]![SiteIsolatedGitHubTokens.SizeInBytes]!.GetValue<long>() +
            artifacts[SiteIsolatedGitHubTokens.ProviderKey]![SiteIsolatedGitHubTokens.SizeInBytes]!.GetValue<long>() +
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
            await Assert.That(actual[SiteIsolatedFields.AggregateJobId]!.GetValue<long>()).IsEqualTo(original[SiteIsolatedFields.AggregateJob]![SiteIsolatedFields.Id]!.GetValue<long>());
            await Assert.That(actual[SiteIsolatedFields.Workers]!.AsArray().Count).IsEqualTo(22);
            return;
        }

        string[] fields =
        [
            SiteIsolatedGitHubTokens.Source, SiteIsolatedGitHubFields.Repository, SiteIsolatedGitHubFields.WorkflowKey,
            SiteIsolatedGitHubTokens.Run, SiteIsolatedGitHubTokens.Cohort, SiteIsolatedGitHubTokens.AggregateJob,
            SiteIsolatedGitHubTokens.Artifacts, SiteIsolatedGitHubTokens.Workers, SiteIsolatedGitHubFields.Preparation,
            SiteIsolatedFields.RuntimeIdentity, SiteIsolatedFields.Functional,
        ];
        foreach (var field in fields)
        {
            await Assert.That(JsonNode.DeepEquals(original[field], actual[field])).IsTrue();
        }

    }

    public static async Task VerifyAsync(JsonObject original, string capture, CancellationToken token)
    {
        await VerifyMetadataAsync(original, capture, token);
        await SiteIsolatedGitHubArchiveReader.VerifyArchiveAsync(Path.Combine(capture,
                SiteIsolatedGitHubTokens.Archives, SiteIsolatedGitHubTokens.Suite), SiteIsolatedGitHubTokens.Suite,
            original, SiteIsolatedGitHubTokens.SuiteKey, SiteIsolatedGitHubTokens.SuiteBytes, token);
        await SiteIsolatedGitHubArchiveReader.VerifyArchiveAsync(Path.Combine(capture,
                SiteIsolatedGitHubTokens.Archives, SiteIsolatedGitHubTokens.Provider), SiteIsolatedGitHubTokens.Provider,
            original, SiteIsolatedGitHubTokens.ProviderKey, SiteIsolatedGitHubTokens.ProviderBytes, token);
    }
}
