using System.Net;
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

    internal static void VerifyMutation(HttpStatusCode status, JsonElement response, Scenario scenario, int expectedCopies)
    {
        if (status == HttpStatusCode.Conflict && scenario == Scenario.DocumentWrite && ErrorIs(response, OpenSearchNames.CreateConflictError))
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.CreateConflict);
        }
        if (status == HttpStatusCode.NotFound && scenario == Scenario.DocumentUpdate && ErrorIs(response, OpenSearchNames.MissingDocumentError))
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.UpdateMissing);
        }
        if (status == HttpStatusCode.NotFound && scenario == Scenario.DocumentDelete &&
            response.TryGetProperty(OpenSearchNames.Result, out var result) && result.GetString() == OpenSearchNames.NotFound)
        {
            Verify(response, expectedCopies);
            throw new ComparisonFailureException(ComparisonMutationFailures.DeleteMissing);
        }
        using var statusResponse = new HttpResponseMessage(status);
        statusResponse.EnsureSuccessStatusCode();
        Verify(response, expectedCopies);
        var expected = scenario switch
        {
            Scenario.DocumentWrite => OpenSearchNames.Created,
            Scenario.DocumentUpdate => OpenSearchNames.Updated,
            Scenario.DocumentDelete => OpenSearchNames.Deleted,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        if (OpenSearchJson.RequiredString(response, OpenSearchNames.Result) != expected)
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.CardinalityMismatch);
        }
    }

    private static bool ErrorIs(JsonElement response, string expected)
        => response.TryGetProperty(OpenSearchNames.Error, out var error) && error.ValueKind == JsonValueKind.Object
            && error.TryGetProperty(OpenSearchNames.ErrorType, out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == expected;
}
