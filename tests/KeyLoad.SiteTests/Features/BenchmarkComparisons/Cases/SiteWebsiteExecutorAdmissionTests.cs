namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteWebsiteExecutorAdmissionTests
{
    [Test]
    public async Task AcBcWeb006AcceptsStandaloneWebsiteExecutorAndPreservesContext()
    {
        var result = await ProbeAsync(SiteIsolatedGitHubFields.WebsiteExecutorIdentity);

        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsTrue();
        var admission = result.GetProperty(SiteIsolatedGitHubFields.Result);
        await Assert.That(admission.GetProperty(SiteIsolatedGitHubFields.CandidateAccepted).GetBoolean()).IsTrue();
        await Assert.That(admission.GetProperty(SiteIsolatedGitHubFields.InputsPreserved).GetBoolean()).IsTrue();
        await Assert.That(admission.GetProperty(SiteIsolatedGitHubFields.FollowupAccepted).GetBoolean()).IsTrue();
    }

    [Test]
    [Arguments(SiteIsolatedGitHubFields.LegacyCiIdentity)]
    [Arguments(SiteIsolatedGitHubFields.WrongWorkflowPathIdentity)]
    public async Task AcBcWeb006RejectsForeignExecutorAndKeepsHealthyFollowup(string identity)
    {
        var result = await ProbeAsync(identity);

        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsTrue();
        var admission = result.GetProperty(SiteIsolatedGitHubFields.Result);
        await Assert.That(admission.GetProperty(SiteIsolatedGitHubFields.CandidateAccepted).GetBoolean()).IsFalse();
        await Assert.That(admission.GetProperty(SiteIsolatedGitHubFields.InputsPreserved).GetBoolean()).IsTrue();
        await Assert.That(admission.GetProperty(SiteIsolatedGitHubFields.FollowupAccepted).GetBoolean()).IsTrue();
    }

    private static Task<System.Text.Json.JsonElement> ProbeAsync(string identity)
    {
        var inputs = SiteContentInputs.FromEnvironment();
        return SiteIsolatedGitHubNodeProcess.RunAsync(inputs.Repository, new
        {
            repository = inputs.Repository,
            operation = SiteIsolatedGitHubFields.ExecutorAdmissionOperation,
            arguments = new { identity },
        }, TestContext.Current!.Execution.CancellationToken);
    }
}
