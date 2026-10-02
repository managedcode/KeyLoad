using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class OpenSearchSearchResponse
{
    internal static void Verify(JsonElement response)
    {
        if (OpenSearchJson.RequiredBoolean(response, OpenSearchNames.SearchTimedOut))
        {
            throw new ComparisonFailureException(OpenSearchNames.SearchShardFailure);
        }

        var shardInfo = OpenSearchJson.RequiredObject(response, OpenSearchNames.ShardsObject);
        if (OpenSearchJson.RequiredInt32(shardInfo, OpenSearchNames.Failed) != 0
            || OpenSearchJson.RequiredInt32(shardInfo, OpenSearchNames.Successful) == 0)
        {
            throw new ComparisonFailureException(OpenSearchNames.SearchShardFailure);
        }

        var hits = OpenSearchJson.RequiredObject(response, OpenSearchNames.Hits).GetProperty(OpenSearchNames.Hits);
        foreach (var hit in hits.EnumerateArray())
        {
            var score = hit.GetProperty(OpenSearchNames.Score).GetDouble();
            if (!double.IsFinite(score))
            {
                throw new ComparisonFailureException(OpenSearchNames.SearchShardFailure);
            }
        }
    }
}
