using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedGitHubFreshnessTests
{
    [Test]
    public async Task AC_ISO_009_PreservesCompleteActualArchiveTupleAndIndependentSourceRevisions()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteIsolatedGitHubScope.CreateAsync(token);
        await WritePublishParserInputsAsync(scope, token);
        var result = await SiteIsolatedGitHubScope.RunAsync(SiteIsolatedGitHubFields.FreshOperation,
            new { before = scope.Before, after = scope.After }, token);
        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsTrue();
        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Result).GetProperty(SiteIsolatedGitHubFields.Fresh).GetBoolean()).IsTrue();
    }

    [Test]
    public async Task AC_ISO_009_ChangedWebsiteSourceCohortJobArtifactOrImageCannotPublish()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteIsolatedGitHubScope.CreateAsync(token);
        Action<JsonObject>[] mutations =
        [
            value => value[SiteIsolatedGitHubTokens.Source]![SiteIsolatedGitHubTokens.Website] = SiteIsolatedGitHubTokens.WrongSha,
            value => value[SiteIsolatedGitHubTokens.Run]![SiteIsolatedGitHubTokens.Attempt] = SiteIsolatedGitHubTokens.Zero,
            value => value[SiteIsolatedGitHubTokens.Run]![SiteIsolatedGitHubTokens.Number] =
                checked(value[SiteIsolatedGitHubTokens.Run]![SiteIsolatedGitHubTokens.Number]!.GetValue<long>() + SiteIsolatedGitHubTokens.One),
            value => value[SiteIsolatedGitHubTokens.Cohort]![SiteIsolatedGitHubTokens.SourceRevision] = SiteIsolatedGitHubTokens.WrongSha,
            value => value[SiteIsolatedGitHubTokens.Workers]![SiteIsolatedGitHubTokens.Zero]![SiteIsolatedGitHubFields.Job]![SiteIsolatedGitHubTokens.Conclusion] = SiteIsolatedGitHubFields.Failure,
            value => value[SiteIsolatedGitHubTokens.Artifacts]![SiteIsolatedGitHubTokens.ProviderKey]![SiteIsolatedGitHubTokens.Expired] = true,
            value => value[SiteIsolatedGitHubFields.Image]![SiteIsolatedGitHubFields.Job]![SiteIsolatedGitHubTokens.Id] = SiteIsolatedGitHubTokens.Zero,
            value => value[SiteIsolatedGitHubTokens.Mode] = SiteIsolatedGitHubTokens.Validate,
        ];
        foreach (var mutate in mutations)
        {
            await WritePublishParserInputsAsync(scope, token);
            var after = JsonNode.Parse(await File.ReadAllBytesAsync(scope.After, token))!.AsObject();
            mutate(after);
            await File.WriteAllTextAsync(scope.After, after.ToJsonString(), token);
            var result = await SiteIsolatedGitHubScope.RunAsync(SiteIsolatedGitHubFields.FreshOperation,
                new { before = scope.Before, after = scope.After }, token);
            await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsFalse();
        }
    }

    private static async Task WritePublishParserInputsAsync(SiteIsolatedGitHubScope scope, CancellationToken token)
    {
        // The copied mode tests eligibility parsing; source, jobs, artifacts and archive identities remain genuine.
        var before = JsonNode.Parse(await File.ReadAllBytesAsync(scope.Inputs.Receipt, token))!.AsObject();
        var after = scope.Inputs.Metadata.DeepClone().AsObject();
        foreach (var value in new[] { before, after })
        {
            value[SiteIsolatedGitHubTokens.Mode] = SiteIsolatedGitHubTokens.Publish;
            value[SiteIsolatedGitHubTokens.PublishEligible] = true;
        }

        await File.WriteAllTextAsync(scope.Before, before.ToJsonString(), token);
        await File.WriteAllTextAsync(scope.After, after.ToJsonString(), token);
    }
}
