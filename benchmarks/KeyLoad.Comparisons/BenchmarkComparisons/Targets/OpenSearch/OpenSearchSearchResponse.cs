using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class OpenSearchSearchResponse
{
    internal static ImmutableArray<FoundDocument> Read(JsonElement response, int topK)
    {
        const int FirstElementIndex = 0;
        const int NoObservedItems = 0;

        Verify(response);
        var aggregation = OpenSearchJson.RequiredPath(response, OpenSearchNames.Aggregations, OpenSearchNames.ExactNeighborsAggregation);
        var rows = OpenSearchJson.RequiredArray(aggregation, OpenSearchNames.AggregationValue);
        if (rows.GetArrayLength() > topK)
        {
            throw new ComparisonFailureException(OpenSearchNames.VectorResponseMismatch);
        }

        var documents = new FoundDocument[rows.GetArrayLength()];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var previousScore = double.PositiveInfinity;
        string? previousId = null;
        var index = FirstElementIndex;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
            {
                throw new ComparisonFailureException(OpenSearchNames.VectorResponseMismatch);
            }
            var score = RequiredScore(row);
            var document = OpenSearchDocument.Read(row);
            if (!seen.Add(document.Id) || score > previousScore
                || (score == previousScore && previousId is not null && StringComparer.Ordinal.Compare(previousId, document.Id) >= NoObservedItems))
            {
                throw new ComparisonFailureException(OpenSearchNames.VectorResponseMismatch);
            }
            documents[index++] = document;
            previousId = document.Id;
            previousScore = score;
        }
        return ImmutableCollectionsMarshal.AsImmutableArray(documents);
    }

    internal static void Verify(JsonElement response)
    {
        const int NoObservedItems = 0;

        if (OpenSearchJson.RequiredBoolean(response, OpenSearchNames.SearchTimedOut))
        {
            throw new ComparisonFailureException(OpenSearchNames.SearchShardFailure);
        }

        var shardInfo = OpenSearchJson.RequiredObject(response, OpenSearchNames.ShardsObject);
        if (OpenSearchJson.RequiredInt32(shardInfo, OpenSearchNames.Failed) != NoObservedItems
            || OpenSearchJson.RequiredInt32(shardInfo, OpenSearchNames.Successful) <= NoObservedItems)
        {
            throw new ComparisonFailureException(OpenSearchNames.SearchShardFailure);
        }
    }

    private static double RequiredScore(JsonElement row)
    {
        var value = OpenSearchJson.Required(row, OpenSearchNames.VectorScore, JsonValueKind.Number);
        if (!value.TryGetDouble(out var score) || !double.IsFinite(score))
        {
            throw new ComparisonFailureException(OpenSearchNames.VectorResponseMismatch);
        }
        return score;
    }
}
