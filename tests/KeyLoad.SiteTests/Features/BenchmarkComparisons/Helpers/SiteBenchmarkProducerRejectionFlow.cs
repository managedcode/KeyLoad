using System.Globalization;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBenchmarkProducerRejectionFlow
{
    internal static async Task RunAsync(SiteIsolatedGitHubScope scope,
        (long RunId, int Attempt, string SourceRevision, string Event, string? Conclusion) producer, CancellationToken token)
    {
        var (runId, attempt, sourceRevision, producerEvent, conclusion) = producer;
        var tokenTuple = new
        {
            runId,
            attempt,
            sourceRevision,
            @event = producerEvent,
            conclusion
        };

        await RejectIdentityAsync(scope, producer, token);
        await RejectStateAsync(scope, producer, token);
        await AssertRejectedAsync(scope, SiteIsolatedGitHubTokens.Publish,
            runId.ToString(CultureInfo.InvariantCulture), tokenTuple, token);
        await AssertRejectedAsync(scope, SiteIsolatedGitHubTokens.Validate, null, tokenTuple, token);
    }

    private static async Task RejectIdentityAsync(SiteIsolatedGitHubScope scope,
        (long RunId, int Attempt, string SourceRevision, string Event, string? Conclusion) producer, CancellationToken token)
    {
        var (runId, attempt, sourceRevision, producerEvent, conclusion) = producer;
        await AssertRejectedAsync(scope, SiteIsolatedGitHubTokens.Publish, null,
            new
            {
                runId = runId + 1,
                attempt,
                sourceRevision,
                @event = producerEvent,
                conclusion
            }, token);
        await AssertRejectedAsync(scope, SiteIsolatedGitHubTokens.Publish, null,
            new
            {
                runId,
                attempt = attempt + 1,
                sourceRevision,
                @event = producerEvent,
                conclusion
            }, token);
        await AssertRejectedAsync(scope, SiteIsolatedGitHubTokens.Publish, null,
            new
            {
                runId,
                attempt,
                sourceRevision = SiteIsolatedGitHubTokens.WrongSha,
                @event = producerEvent,
                conclusion
            }, token);
    }

    private static async Task RejectStateAsync(SiteIsolatedGitHubScope scope,
        (long RunId, int Attempt, string SourceRevision, string Event, string? Conclusion) producer, CancellationToken token)
    {
        var (runId, attempt, sourceRevision, producerEvent, conclusion) = producer;
        await AssertRejectedAsync(scope, SiteIsolatedGitHubTokens.Publish, null,
            new
            {
                runId,
                attempt,
                sourceRevision,
                @event = SiteIsolatedGitHubFields.WorkflowRun,
                conclusion
            }, token);
        var otherEvent = producerEvent == "push" ? "workflow_dispatch" : "push";
        await AssertRejectedAsync(scope, SiteIsolatedGitHubTokens.Publish, null,
            new
            {
                runId,
                attempt,
                sourceRevision,
                @event = otherEvent,
                conclusion
            }, token);
        await AssertRejectedAsync(scope, SiteIsolatedGitHubTokens.Publish, null,
            new
            {
                runId,
                attempt,
                sourceRevision,
                @event = producerEvent,
                conclusion = SiteIsolatedGitHubFields.Cancelled
            }, token);
    }

    internal static Task<JsonElement> SelectAsync(SiteIsolatedGitHubScope scope, string mode,
        string? requestedRun, object? producer, CancellationToken token) => SiteIsolatedGitHubScope.RunAsync(
        SiteIsolatedGitHubFields.SelectionOperation,
        new
        {
            input = scope.Capture,
            mode,
            requestedRun,
            producer
        }, token);

    private static async Task AssertRejectedAsync(SiteIsolatedGitHubScope scope, string mode,
        string? requestedRun, object? producer, CancellationToken token)
    {
        var result = await SelectAsync(scope, mode, requestedRun, producer, token);
        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsFalse();
    }
}
