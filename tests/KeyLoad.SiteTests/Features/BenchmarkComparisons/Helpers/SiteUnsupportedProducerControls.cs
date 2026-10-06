using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteUnsupportedProducerControls
{
    internal static Task<JsonElement> SelectAsync(SiteIsolatedGitHubScope scope,
        SiteUnsupportedProducerCandidate producer, CancellationToken token) => SelectAsync(scope, producer, true, token);

    internal static Task<JsonElement> SelectAsync(SiteIsolatedGitHubScope scope,
        SiteUnsupportedProducerCandidate producer, bool optional, CancellationToken token)
        => SiteIsolatedGitHubScope.RunAsync(SiteIsolatedGitHubFields.SelectionOperation, new
        {
            input = scope.Capture,
            mode = SiteIsolatedGitHubTokens.Publish,
            requestedRun = (string?)null,
            producer = new
            {
                runId = producer.RunId,
                attempt = producer.Attempt,
                sourceRevision = producer.SourceRevision,
                @event = producer.Event,
                conclusion = producer.Conclusion,
            },
            optional,
        }, token);
}
