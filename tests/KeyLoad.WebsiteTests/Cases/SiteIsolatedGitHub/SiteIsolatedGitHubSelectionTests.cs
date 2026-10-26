namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed class SiteIsolatedGitHubSelectionTests
{
    [Test]
    public async Task AC_ISO_009_SelectsActualSuccessfulSessionsWithoutRequiringOverallCiSuccess()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteIsolatedGitHubScope.CreateAsync(token);
        var result = await scope.SelectAsync(token);
        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsTrue();
        var selected = result.GetProperty(SiteIsolatedGitHubFields.Result);
        await Assert.That(selected.GetProperty(SiteIsolatedGitHubFields.State).GetString())
            .IsEqualTo(SiteIsolatedGitHubFields.Selected);
        await Assert.That(selected.GetProperty(SiteIsolatedGitHubSelectionFields.RunId).GetInt64())
            .IsEqualTo(scope.Inputs.Metadata[SiteIsolatedGitHubTokens.Run]![SiteIsolatedGitHubTokens.Id]!.GetValue<long>());
        await Assert.That(selected.GetProperty(SiteIsolatedGitHubSelectionFields.PreparationJobId).GetInt64())
            .IsEqualTo(scope.Inputs.Metadata[SiteIsolatedGitHubFields.Preparation]![SiteIsolatedGitHubFields.Job]![SiteIsolatedGitHubTokens.Id]!.GetValue<long>());
    }

    [Test]
    public async Task AC_ISO_009_SuccessfulPreparationRejectsMissingMandatoryStep()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteIsolatedGitHubScope.CreateAsync(token);
        var id = scope.Inputs.Metadata[SiteIsolatedGitHubFields.Preparation]![SiteIsolatedGitHubFields.Job]![SiteIsolatedGitHubTokens.Id]!.GetValue<long>();
        await SiteIsolatedGitHubScope.MutateAsync(scope.SelectedJobsPath, pages =>
        {
            var job = pages.SelectMany(page => page![SiteIsolatedGitHubSelectionFields.Jobs]!.AsArray())
                .Single(value => value![SiteIsolatedGitHubTokens.Id]!.GetValue<long>() == id)!;
            var steps = job[SiteIsolatedGitHubFields.Steps]!.AsArray();
            var required = "Build the comparison harness";
            steps.Remove(steps.Single(step => step![SiteIsolatedGitHubFields.Name]!.GetValue<string>() == required));
        }, token);
        await Assert.That((await scope.SelectAsync(token)).GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsFalse();
    }

    [Test]
    public async Task AC_ISO_007_CurrentSessionAndRuntimeProofRejectsExpiredWorkerArtifact()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteIsolatedGitHubScope.CreateAsync(token);
        await Assert.That((await scope.ProveAsync(token)).GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsTrue();
        var id = scope.Inputs.Metadata[SiteIsolatedGitHubTokens.Workers]![0]![SiteIsolatedFields.Artifact]![SiteIsolatedGitHubTokens.Id]!.GetValue<long>();
        await SiteIsolatedGitHubScope.MutateAsync(scope.ArtifactsPath, pages =>
        {
            var artifact = pages.SelectMany(page => page![SiteIsolatedGitHubTokens.Artifacts]!.AsArray())
                .Single(value => value![SiteIsolatedGitHubTokens.Id]!.GetValue<long>() == id)!;
            artifact[SiteIsolatedGitHubTokens.Expired] = true;
        }, token);
        await Assert.That((await scope.ProveAsync(token)).GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsFalse();
    }
}
