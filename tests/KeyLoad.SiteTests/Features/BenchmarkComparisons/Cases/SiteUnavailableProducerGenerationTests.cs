using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteUnavailableProducerGenerationTests
{
    [Test]
    public async Task AcBcWeb002OriginalCe2AggregateRemainsUnavailableWithoutMutation()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var inputs = SiteContentInputs.FromEnvironment();
        var bytes = await SiteUnavailableProducerCe2Fixture.ReadAsync(inputs, token);
        using var fixture = JsonDocument.Parse(bytes);
        var result = await ProbeAsync(inputs, fixture.RootElement, mutation: null, token);

        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsTrue();
        var selection = result.GetProperty(SiteIsolatedGitHubFields.Result);
        await Assert.That(selection.GetProperty(SiteUnavailableProducerCe2Fixture.CandidateAcceptedField).GetBoolean()).IsTrue();
        await Assert.That(selection.GetProperty(SiteUnavailableProducerCe2Fixture.UnavailableField).GetBoolean()).IsTrue();
        await Assert.That(selection.GetProperty(SiteUnavailableProducerCe2Fixture.InputsPreservedField).GetBoolean()).IsTrue();
        await Assert.That(selection.GetProperty(SiteUnavailableProducerCe2Fixture.CandidatePreservedField).GetBoolean()).IsTrue();
        await Assert.That(selection.GetProperty(SiteUnavailableProducerCe2Fixture.FollowupUnavailableField).GetBoolean()).IsTrue();
        await SiteUnavailableProducerCe2Fixture.VerifyUnchangedAsync(inputs, bytes, token);
    }

    [Test]
    [Arguments(SiteUnavailableProducerCe2Fixture.ChangedStepMutation)]
    [Arguments(SiteUnavailableProducerCe2Fixture.ChangedSourceMutation)]
    [Arguments(SiteUnavailableProducerCe2Fixture.ChangedRepositoryMutation)]
    public async Task AcBcWeb002RejectsMutatedOriginalAndPreservesHealthyFollowup(string mutation)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var inputs = SiteContentInputs.FromEnvironment();
        var bytes = await SiteUnavailableProducerCe2Fixture.ReadAsync(inputs, token);
        using var fixture = JsonDocument.Parse(bytes);
        var result = await ProbeAsync(inputs, fixture.RootElement, mutation, token);

        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsTrue();
        var selection = result.GetProperty(SiteIsolatedGitHubFields.Result);
        await Assert.That(selection.GetProperty(SiteUnavailableProducerCe2Fixture.CandidateAcceptedField).GetBoolean()).IsFalse();
        await Assert.That(selection.GetProperty(SiteUnavailableProducerCe2Fixture.UnavailableField).GetBoolean()).IsFalse();
        await Assert.That(selection.GetProperty(SiteUnavailableProducerCe2Fixture.InputsPreservedField).GetBoolean()).IsTrue();
        await Assert.That(selection.GetProperty(SiteUnavailableProducerCe2Fixture.CandidatePreservedField).GetBoolean()).IsTrue();
        await Assert.That(selection.GetProperty(SiteUnavailableProducerCe2Fixture.FollowupUnavailableField).GetBoolean()).IsTrue();
        await SiteUnavailableProducerCe2Fixture.VerifyUnchangedAsync(inputs, bytes, token);
    }

    private static Task<JsonElement> ProbeAsync(SiteContentInputs inputs, JsonElement fixture, string? mutation,
        CancellationToken token) => SiteIsolatedGitHubNodeProcess.RunAsync(inputs.Repository, new
        {
            repository = inputs.Repository,
            operation = SiteIsolatedGitHubFields.UnavailableProducerGenerationOperation,
            arguments = new { fixture, mutation, changedSource = SiteIsolatedGitHubTokens.WrongSha },
        }, token);
}
