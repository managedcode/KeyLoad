using System.Net;
using System.Text;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Creates a batch of new documents through one _bulk request with the single create's acknowledgement contract.</summary>
internal static class OpenSearchBulkCreate
{
    private const string CreateAction = "create";
    private const char LineFeed = '\n';

    internal static async Task CreateAsync(HttpClient client, string index, IReadOnlyList<BenchmarkDocument> documents, int expectedCopies,
        ComparisonExecutionOptions policy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);
        // The bulk path keeps wait_for_active_shards=all and the default refresh=false of the single _create request.
        using var response = await OpenSearchHttp.SendBulkAsync(client, OpenSearchNames.PathSeparator + index + OpenSearchNames.BulkSuffix,
            Body(index, documents), policy, cancellationToken);
        Verify(response.RootElement, documents.Count, expectedCopies);
    }

    private static string Body(string index, IReadOnlyList<BenchmarkDocument> documents)
    {
        var body = new StringBuilder();
        foreach (var document in documents)
        {
            body.Append(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                [CreateAction] = new Dictionary<string, string>
                {
                    [OpenSearchNames.IndexActionName] = index,
                    [OpenSearchNames.DocumentActionId] = document.Id
                }
            }, OpenSearchHttp.JsonOptions)).Append(LineFeed);
            body.Append(JsonSerializer.Serialize(OpenSearchDocument.CreateWithoutVector(document.Id, document.Json), OpenSearchHttp.JsonOptions))
                .Append(LineFeed);
        }

        return body.ToString();
    }

    // Every item is a create with its own shard acknowledgement; an existing ID fails exactly as the single create does.
    private static void Verify(JsonElement response, int expectedItems, int expectedCopies)
    {
        var items = OpenSearchJson.RequiredArray(response, OpenSearchNames.Items);
        if (items.GetArrayLength() != expectedItems)
        {
            throw new ComparisonFailureException(OpenSearchNames.BulkItemFailure);
        }

        foreach (var item in items.EnumerateArray())
        {
            var result = OpenSearchJson.RequiredObject(item, CreateAction);
            var status = OpenSearchJson.RequiredInt32(result, OpenSearchNames.StatusCode);
            if (status == (int)HttpStatusCode.Conflict && OpenSearchWriteAcknowledgement.ErrorIs(result, OpenSearchNames.CreateConflictError))
            {
                throw new ComparisonFailureException(ComparisonMutationFailures.CreateConflict);
            }

            if (status != (int)HttpStatusCode.Created || OpenSearchJson.RequiredString(result, OpenSearchNames.Result) != OpenSearchNames.Created)
            {
                throw new ComparisonFailureException(OpenSearchNames.BulkItemFailure);
            }

            OpenSearchWriteAcknowledgement.Verify(result, expectedCopies);
        }

        if (OpenSearchJson.RequiredBoolean(response, OpenSearchNames.BulkErrors))
        {
            throw new ComparisonFailureException(OpenSearchNames.BulkItemFailure);
        }
    }
}
