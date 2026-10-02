using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class OpenSearchWriteAcknowledgement
{
    internal static void Verify(JsonElement response, int expectedCopies)
    {
        var shards = OpenSearchJson.RequiredObject(response, OpenSearchNames.ShardsObject);
        var total = OpenSearchJson.RequiredInt32(shards, OpenSearchNames.Total);
        var successful = OpenSearchJson.RequiredInt32(shards, OpenSearchNames.Successful);
        var failed = OpenSearchJson.RequiredInt32(shards, OpenSearchNames.Failed);
        if (total != expectedCopies || successful != expectedCopies || failed != OpenSearchNames.NoFailures)
        {
            throw new ComparisonFailureException(OpenSearchNames.WriteShardAcknowledgementMismatch);
        }
    }
}
