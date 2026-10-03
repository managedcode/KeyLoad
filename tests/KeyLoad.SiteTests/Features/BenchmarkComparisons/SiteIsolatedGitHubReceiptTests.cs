using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedGitHubReceiptTests
{
    [Test]
    public async Task AC_ISO_009_ActualMetadataRetainsIndependentSourcesAndAllNativeWorkers()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var inputs = await SiteIsolatedGitHubInputs.ReadAsync(token);
        var result = await ProbeAsync(inputs.Metadata, token);
        await Assert.That(result).IsTrue();
        await Assert.That(inputs.Metadata[SiteIsolatedGitHubTokens.Workers]!.AsArray().Count)
            .IsEqualTo(SiteIsolatedGitHubTokens.WorkerCount);
        var actualSite = SiteTestInputs.Read();
        await Assert.That(inputs.Metadata[SiteIsolatedGitHubTokens.Source]![SiteIsolatedGitHubTokens.Website]!.GetValue<string>())
            .IsEqualTo(actualSite.SiteRevision);
    }

    [Test]
    public async Task AC_ISO_009_RejectsClosedReceiptSourceIdentityExpiryAndCompletenessCorruption()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var inputs = await SiteIsolatedGitHubInputs.ReadAsync(token);
        Action<JsonObject>[] changes =
        [
            value => value[SiteIsolatedGitHubTokens.Extra] = true,
            value => value[SiteIsolatedGitHubTokens.SchemaVersion] = SiteIsolatedGitHubTokens.Zero,
            value => value[SiteIsolatedGitHubTokens.Source]![SiteIsolatedGitHubTokens.Measured] = SiteIsolatedGitHubTokens.WrongSha,
            value => value[SiteIsolatedGitHubTokens.Artifacts]![SiteIsolatedGitHubTokens.SuiteKey]![SiteIsolatedGitHubTokens.Expired] = true,
            value => value[SiteIsolatedGitHubTokens.Workers]!.AsArray().RemoveAt(SiteIsolatedGitHubTokens.Zero),
            value => value[SiteIsolatedGitHubTokens.Workers]!.AsArray().Add(value[SiteIsolatedGitHubTokens.Workers]![SiteIsolatedGitHubTokens.Zero]!.DeepClone()),
            value => value[SiteIsolatedGitHubTokens.PublishEligible] = value[SiteIsolatedGitHubTokens.Mode]!.GetValue<string>() != SiteIsolatedGitHubTokens.Publish,
        ];
        foreach (var change in changes)
        {
            var copy = inputs.Metadata.DeepClone().AsObject();
            change(copy);
            await Assert.That(await ProbeAsync(copy, token)).IsFalse();
        }
    }

    private static async Task<bool> ProbeAsync(JsonObject receipt, CancellationToken token)
    {
        var inputs = SiteTestInputs.Read();
        var result = await SiteIsolatedGitHubNodeProcess.RunAsync(inputs.Repository, new
        {
            operation = SiteIsolatedGitHubFields.ReceiptOperation,
            repository = inputs.Repository,
            receipt,
        }, token);
        return result.GetProperty(SiteIsolatedFields.Ok).GetBoolean();
    }
}
