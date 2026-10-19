using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal static class OpenSearchSessionQueryExecution
{
    private const string SessionPathPrefix = "/";
    private const string SessionMappingSuffix = "/_mapping";
    private const string SessionBackfillSuffix = "/_update_by_query?wait_for_completion=true&refresh=true&wait_for_active_shards=all&conflicts=abort";
    private const string SessionAcknowledgedProperty = "acknowledged";
    private const string SessionFailuresProperty = "failures";
    private const string SessionHitsProperty = "hits";
    private const string SessionIdField = "id.session";
    private const string SessionKeywordType = "keyword";
    private const string SessionMappingNotAcknowledged = "SessionIndexMappingNotAcknowledged";
    private const string SessionBackfillIncomplete = "SessionIndexBackfillIncomplete";
    private const string SessionShardFailure = "SessionQueryShardFailure";
    private const string SessionCardinalityMismatch = "SessionQueryCardinalityMismatch";
    private const int SessionCorpusRows = 1_000_000;
    private const int SessionEmptyFailures = 0;
    private const int SessionCandidateLimit = 2;
    private const int SessionRangeWidth = 1;
    private const int SessionExpectedRows = 1;
    private const int SessionFirstRow = 0;

    /// <inheritdoc/>
    internal static async Task BuildIndexesAsync(HttpClient client, string index, TimeProvider timeProvider,
        IOptions<ComparisonExecutionOptions> executionOptions, CancellationToken cancellationToken)
    {
        using var deadline = new ComparisonCancellationSource(timeProvider, cancellationToken);
        deadline.CancelAfter(executionOptions.Value.IndexBuildTimeout);
        using (var mapping = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Put, SessionPathPrefix + index + SessionMappingSuffix,
            new
            {
                properties = new
                {
                    id = new
                    {
                        type = OpenSearchNames.Text,
                        fields = new
                        {
                            session = new
                            {
                                type = SessionKeywordType
                            }
                        }
                    }
                }
            }, deadline.Token).ConfigureAwait(false))
        {
            if (!mapping.RootElement.GetProperty(SessionAcknowledgedProperty).GetBoolean())
            {
                throw new ComparisonFailureException(SessionMappingNotAcknowledged);
            }
        }
        // Execution in-place backfill indexes the retained documents; no new namespace, corpus reload or hidden retry.
        using var rebuilt = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Post,
            SessionPathPrefix + index + SessionBackfillSuffix,
            new
            {
                query = new
                {
                    match_all = new
                    {
                    }
                }
            }, deadline.Token).ConfigureAwait(false);
        var result = rebuilt.RootElement;
        if (result.GetProperty(OpenSearchNames.SearchTimedOut).GetBoolean() || result.GetProperty(OpenSearchNames.Total).GetInt32() != SessionCorpusRows
            || result.GetProperty(OpenSearchNames.Updated).GetInt32() != SessionCorpusRows || result.GetProperty(SessionFailuresProperty).GetArrayLength() != SessionEmptyFailures)
        {
            throw new ComparisonFailureException(SessionBackfillIncomplete);
        }
    }

    /// <inheritdoc/>
    internal static async Task QueryAsync(HttpClient client, string index, int ordinal,
        int stableStart, int stableCount, CancellationToken cancellationToken)
    {
        var expected = BenchmarkSessionQueryContract.Expected(ordinal, stableStart, stableCount);
        var body = new
        {
            size = SessionCandidateLimit,
            track_total_hits = true,
            query = new
            {
                @bool = new
                {
                    filter = new object[]
                    {
                        new { term = new Dictionary<string, string> { [SessionIdField] = expected.Id } },
                        new
                        {
                            range = new Dictionary<string, object>
                            {
                                [SessionIdField] = new
                                {
                                    gte = expected.Id,
                                    lt = ScaledComparisonCorpus.Id(expected.Number + SessionRangeWidth)
                                }
                            }
                        }
                    }
                }
            }
        };
        using var response = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Post, SessionPathPrefix + index + OpenSearchNames.SearchSuffix, body, cancellationToken).ConfigureAwait(false);
        var root = response.RootElement;
        if (root.GetProperty(OpenSearchNames.SearchTimedOut).GetBoolean() || root.GetProperty(OpenSearchNames.ShardsObject).GetProperty(OpenSearchNames.Failed).GetInt32() != SessionEmptyFailures)
        {
            throw new ComparisonFailureException(SessionShardFailure);
        }
        var hits = root.GetProperty(SessionHitsProperty);
        var rows = hits.GetProperty(SessionHitsProperty);
        if (hits.GetProperty(OpenSearchNames.Total).GetProperty(OpenSearchNames.AggregationValue).GetInt32() != SessionExpectedRows || rows.GetArrayLength() != SessionExpectedRows)
        {
            throw new ComparisonFailureException(SessionCardinalityMismatch);
        }
        BenchmarkSessionQueryContract.Require(OpenSearchDocument.Read(rows[SessionFirstRow].GetProperty(OpenSearchNames.Source)), expected);
    }
}
