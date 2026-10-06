using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteBenchmarkLatestProducerTests
{
    [Test]
    [Arguments("newerPending")]
    [Arguments("olderOtherEvent")]
    [Arguments("cancelledLatest")]
    public async Task AcBcWeb002LatestCompletedProducerSpansPushAndManualWhileIgnoringPending(string scenario)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteIsolatedGitHubScope.CreateAsync(token);
        var original = scope.Inputs.Metadata.DeepClone();
        await SiteBenchmarkLatestProducerFixture.PrepareAsync(scope, scenario, token);
        var result = await ProbeAsync(scope, token);
        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsTrue();
        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Result)
            .GetProperty(SiteIsolatedGitHubFields.State).GetString()).IsEqualTo(SiteIsolatedGitHubFields.Selected);
        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Result)
            .GetProperty(SiteIsolatedGitHubSelectionFields.RunId).GetInt64()).IsEqualTo(
                scope.Inputs.Metadata[SiteIsolatedGitHubTokens.Run]![SiteIsolatedGitHubTokens.Id]!.GetValue<long>());
        await Assert.That(JsonNode.DeepEquals(scope.Inputs.Metadata, original)).IsTrue();
    }

    [Test]
    [Arguments("failedAggregate")]
    [Arguments("missingAggregate")]
    [Arguments("noCompleted")]
    public async Task AcBcWeb002InvalidLatestCompletedProducerCannotFallBackToOlderMeasurements(string scenario)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteIsolatedGitHubScope.CreateAsync(token);
        var original = scope.Inputs.Metadata.DeepClone();
        await SiteBenchmarkLatestProducerFixture.PrepareAsync(scope, scenario, token);
        var result = await ProbeAsync(scope, token);
        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsFalse();
        await Assert.That(JsonNode.DeepEquals(scope.Inputs.Metadata, original)).IsTrue();
    }

    private static Task<JsonElement> ProbeAsync(SiteIsolatedGitHubScope scope, CancellationToken token) =>
        SiteIsolatedGitHubScope.RunAsync("latest-selection", new { input = scope.Capture }, token);
}
