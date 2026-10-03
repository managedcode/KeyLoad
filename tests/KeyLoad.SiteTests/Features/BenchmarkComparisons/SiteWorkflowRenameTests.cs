using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteWorkflowRenameTests
{
    [Test]
    public async Task AcWf003UsesCurrentCiMetadataAndPreservesHistoricalLegacyRunName()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
        var workflow = (JsonObject)await scope.ReadCaptureJsonAsync(SiteGitHubEvidenceTokens.WorkflowCapture, token);
        await Assert.That(workflow[SiteTokens.Name]!.GetValue<string>())
            .IsEqualTo(SiteGitHubEvidenceTokens.WorkflowDisplayName);
        await Assert.That(scope.Expected.Run.GetProperty(SiteTokens.Name).GetString())
            .IsEqualTo(SiteGitHubEvidenceTokens.WorkflowName);

        var selected = await scope.SelectAsync(SiteGitHubEvidenceTokens.ModePublish, null, token);
        await Assert.That(selected.Accepted).IsTrue();
        await Assert.That(selected.Value.GetProperty(SiteGitHubEvidenceTokens.State).GetString())
            .IsEqualTo(SiteGitHubEvidenceTokens.Selected);

        workflow[SiteTokens.Name] = SiteGitHubEvidenceTokens.WorkflowName;
        await scope.WriteCaptureJsonAsync(SiteGitHubEvidenceTokens.WorkflowCapture, workflow, token);
        var rejected = await scope.SelectAsync(SiteGitHubEvidenceTokens.ModePublish, null, token);
        await Assert.That(rejected.Accepted).IsFalse();
        await Assert.That(rejected.ErrorCode).IsEqualTo(SiteGitHubEvidenceTokens.ErrorWorkflow);
    }
}
