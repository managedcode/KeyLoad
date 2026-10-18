using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal static class OpenSearchDocumentCopyProof
{
    private const int PrimaryCopyCount = 1, NoFailures = 0, NoHits = 0, SingleSortField = 1, FirstSortIndex = 0, NoOrdinalAdvance = 0;
    private const string HitsField = "hits", SizeField = "size", QueryField = "query", SortField = "sort", IdSortField = "id.keyword";
    private const string AscendingSort = "asc", SearchAfterField = "search_after", TrackTotalHitsField = "track_total_hits";
    private const string RelationField = "relation", ExactTotalRelation = "eq";
    private const string FinalFlushSuffix = "/_flush?wait_if_ongoing=true&force=true";
    private const string CopySearchSuffix = "/_search?allow_partial_search_results=false&preference=";
    private const string OnlyNodePreference = "_only_nodes:";
    private const string FinalCopyObservation = "Final document shard copy node={0}; records={1}; sha256={2}; native _only_nodes full ordered readback";
    private const string FinalFlushObservation = "Final native flush and refresh acknowledged all {0} shard copies after joined measured operations and primary oracle";
    private const string Failure = "OpenSearchFinalDocumentCopyReceiptMissing";
    private static readonly CompositeFormat FinalCopyObservationFormat = CompositeFormat.Parse(FinalCopyObservation);
    private static readonly CompositeFormat FinalFlushObservationFormat = CompositeFormat.Parse(FinalFlushObservation);
    internal static async Task<ClusterEvidence> VerifyAsync(HttpClient client, string index, int copies,
        ComparisonTopology topology, TargetProfile initial, DocumentComparisonSchedule schedule,
        IOptions<NativeComparisonExecutionOptions> executionOptions, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var before = await OpenSearchClusterEvidence.ObserveAsync(client, index, copies, topology, executionOptions, token).ConfigureAwait(false);
        var initialCluster = initial.Cluster ?? throw new ComparisonFailureException(Failure);
        var initialNodes = initialCluster.Observations.SingleOrDefault(value => value.StartsWith(OpenSearchNames.EvidenceNodeIds, StringComparison.Ordinal));
        if (initialNodes != OpenSearchNames.EvidenceNodeIds + string.Join(OpenSearchNames.CommaSeparator, before.NodeIds.Order(StringComparer.Ordinal)))
        {
            throw new ComparisonFailureException(Failure);
        }

        using (var flush = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Post, OpenSearchNames.PathSeparator + index + FinalFlushSuffix, null, token).ConfigureAwait(false))
        {
            RequireShardReceipt(flush.RootElement, copies);
        }

        using (var refresh = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Post, OpenSearchNames.PathSeparator + index + OpenSearchNames.RefreshSuffix, null, token).ConfigureAwait(false))
        {
            RequireShardReceipt(refresh.RootElement, copies);
        }

        var settings = await OpenSearchIndex.VerifySettingsAsync(client, index, copies - PrimaryCopyCount, token).ConfigureAwait(false);
        var observations = new List<string> { OpenSearchNames.EvidenceSettings + settings };
        foreach (var nodeId in before.NodeIds.Order(StringComparer.Ordinal))
        {
            var evidence = await DocumentComparisonOracle.VerifyAsync(ReadCopyAsync(client, index, nodeId, schedule.Final,
                executionOptions.Value.ReadbackBatchCapacity, token), schedule.FinalDocuments(), token).ConfigureAwait(false);
            observations.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, FinalCopyObservationFormat, nodeId, evidence.Records, evidence.ActualSha256));
        }
        var after = await OpenSearchClusterEvidence.ObserveAsync(client, index, copies, topology, executionOptions, token).ConfigureAwait(false);
        RequireUnchangedMembership(before.NodeIds, before.ShardPlacements, after.NodeIds, after.ShardPlacements);
        observations.Add(OpenSearchNames.EvidenceNodeIds + string.Join(OpenSearchNames.CommaSeparator, after.NodeIds.Order(StringComparer.Ordinal)));
        observations.Add(OpenSearchNames.EvidencePlacements + string.Join(OpenSearchNames.CommaSeparator, after.ShardPlacements.Order(StringComparer.Ordinal)));
        observations.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, FinalFlushObservationFormat, copies));
        return new(after.NodeIds.Length, after.ActiveCopies, after.Status, observations.ToImmutableArray());
    }
    internal static void RequireUnchangedMembership(string[] beforeNodes, string[] beforePlacements, string[] afterNodes, string[] afterPlacements)
    {
        if (!beforeNodes.Order(StringComparer.Ordinal).SequenceEqual(afterNodes.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || !beforePlacements.Order(StringComparer.Ordinal).SequenceEqual(afterPlacements.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new ComparisonFailureException(Failure);
        }
    }
    internal static void RequireShardReceipt(JsonElement response, int copies)
    {
        var shards = OpenSearchJson.RequiredObject(response, OpenSearchNames.ShardsObject);
        if (OpenSearchJson.RequiredInt32(shards, OpenSearchNames.Total) != copies || OpenSearchJson.RequiredInt32(shards, OpenSearchNames.Successful) != copies
            || OpenSearchJson.RequiredInt32(shards, OpenSearchNames.Failed) != NoFailures)
        {
            throw new ComparisonFailureException(Failure);
        }
    }
    private static async IAsyncEnumerable<FoundDocument> ReadCopyAsync(HttpClient client, string index, string nodeId,
        int expectedRecords, int capacity, [EnumeratorCancellation] CancellationToken token)
    {
        string? searchAfter = null;
        while (true)
        {
            var body = new Dictionary<string, object>
            {
                [SizeField] = capacity,
                [TrackTotalHitsField] = true,
                [QueryField] = new { match_all = new { } },
                [SortField] = new object[] { new Dictionary<string, string> { [IdSortField] = AscendingSort } }
            };
            if (searchAfter is not null)
            {
                body[SearchAfterField] = new[] { searchAfter };
            }

            using var response = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Post,
                OpenSearchNames.PathSeparator + index + CopySearchSuffix + Uri.EscapeDataString(OnlyNodePreference + nodeId), body, token).ConfigureAwait(false);
            var root = response.RootElement;
            RequireShardReceipt(root, PrimaryCopyCount);
            if (OpenSearchJson.RequiredBoolean(root, OpenSearchNames.SearchTimedOut))
            {
                throw new ComparisonFailureException(Failure);
            }

            var hits = OpenSearchJson.RequiredObject(root, HitsField);
            var total = OpenSearchJson.RequiredObject(hits, OpenSearchNames.Total);
            if (OpenSearchJson.RequiredInt32(total, OpenSearchNames.AggregationValue) != expectedRecords || OpenSearchJson.RequiredString(total, RelationField) != ExactTotalRelation)
            {
                throw new ComparisonFailureException(Failure);
            }

            var values = OpenSearchJson.RequiredArray(hits, HitsField);
            if (values.GetArrayLength() > capacity)
            {
                throw new ComparisonFailureException(Failure);
            }

            if (values.GetArrayLength() == NoHits)
            {
                yield break;
            }

            foreach (var hit in values.EnumerateArray())
            {
                token.ThrowIfCancellationRequested();
                var value = OpenSearchDocument.Read(hit.GetProperty(OpenSearchNames.Source));
                var sort = OpenSearchJson.RequiredArray(hit, SortField);
                if (sort.GetArrayLength() != SingleSortField || sort[FirstSortIndex].ValueKind != JsonValueKind.String || sort[FirstSortIndex].GetString() != value.Id
                    || OpenSearchJson.RequiredString(hit, OpenSearchNames.DocumentActionId) != value.Id
                    || (searchAfter is not null && StringComparer.Ordinal.Compare(value.Id, searchAfter) <= NoOrdinalAdvance))
                {
                    throw new ComparisonFailureException(Failure);
                }

                searchAfter = value.Id;
                yield return value;
            }
        }
    }
}
