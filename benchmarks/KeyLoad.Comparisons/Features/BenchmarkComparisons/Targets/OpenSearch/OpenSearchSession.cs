using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal sealed class OpenSearchSession(HttpClient client, string index, int topK, int expectedCopies, int corpusCount, IOptions<NativeComparisonExecutionOptions> executionOptions) : IComparisonSession
{
    private readonly NativeComparisonExecutionOptions execution = NativeComparisonExecutionOptions.Require(executionOptions).Value;

    private const string OpenSearchCorpusSortValueInvalidDetail = "OpenSearchCorpusSortValueInvalid";

    private const string HitsProperty = "hits";
    private const string IdKeywordField = "id.keyword";
    private const string QueryProperty = "query";
    private const string SearchAfterProperty = "search_after";
    private const string SizeProperty = "size";
    private const string SortProperty = "sort";
    public async IAsyncEnumerable<FoundDocument> ReadCorpusAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const int NoObservedItems = 0;
        const string AscToken = "asc";
        const int NoItems = 0;
        const string ScaledCorpusReadbackExtraRecordDetail = "ScaledCorpusReadbackExtraRecord";
        const int SingleItemCount = 1;
        const string ScaledCorpusReadbackCountMismatchDetail = "ScaledCorpusReadbackCountMismatch";

        object?[]? searchAfter = null;
        var seen = NoObservedItems;
        while (true)
        {
            var body = new Dictionary<string, object>
            {
                [SizeProperty] = execution.ReadbackBatchCapacity,
                [SortProperty] = new object[] { new Dictionary<string, string> { [IdKeywordField] = AscToken } },
                [QueryProperty] = new { match_all = new { } }
            };
            if (searchAfter is not null)
            {
                body[SearchAfterProperty] = searchAfter;
            }
            using var response = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Post,
                OpenSearchNames.PathSeparator + index + OpenSearchNames.SearchSuffix, body, cancellationToken);
            var hits = response.RootElement.GetProperty(HitsProperty).GetProperty(HitsProperty);
            if (hits.GetArrayLength() == NoItems)
            {
                break;
            }
            foreach (var hit in hits.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (seen >= corpusCount)
                {
                    throw new ComparisonFailureException(ScaledCorpusReadbackExtraRecordDetail);
                }
                yield return OpenSearchDocument.Read(hit.GetProperty(OpenSearchNames.Source));
                seen++;
            }
            var lastSort = hits[hits.GetArrayLength() - SingleItemCount].GetProperty(SortProperty);
            searchAfter = lastSort.EnumerateArray().Select(ReadSortValue).ToArray();
        }
        if (seen != corpusCount)
        {
            throw new ComparisonFailureException(ScaledCorpusReadbackCountMismatchDetail);
        }
    }

    private static object? ReadSortValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetInt64(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => throw new ComparisonFailureException(OpenSearchCorpusSortValueInvalidDetail)
    };

    public async Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        using var result = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Get,
            OpenSearchNames.PathSeparator + index + OpenSearchNames.DocumentPath + Uri.EscapeDataString(document.Id) + OpenSearchNames.RealtimeQuery, null,
            cancellationToken, allowNotFound: true);
        if (!OpenSearchJson.OptionalBoolean(result.RootElement, OpenSearchNames.Found))
        {
            return null;
        }

        return OpenSearchDocument.Read(result.RootElement.GetProperty(OpenSearchNames.Source));
    }

    public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken)
    {
        return scenario switch
        {
            Scenario.PointRead => new(Document: await ReadAsync(document, cancellationToken)),
            Scenario.DocumentWrite => await WriteAsync(document, cancellationToken),
            Scenario.DocumentUpdate => await UpdateAsync(document, cancellationToken),
            Scenario.DocumentDelete => await DeleteAsync(document, cancellationToken),
            Scenario.VectorExact => await SearchAsync(document, cancellationToken),
            _ => throw new NotSupportedException(OpenSearchNames.Unsupported)
        };
    }

    private async Task<OperationResult> WriteAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        var source = OpenSearchDocument.CreateWithoutVector(document.Id, document.Json);
        using var response = await OpenSearchHttp.SendMutationAsync(client, HttpMethod.Put,
            OpenSearchNames.PathSeparator + index + OpenSearchNames.CreateDocumentPath + Uri.EscapeDataString(document.Id) + OpenSearchNames.CreateIndexSuffix,
            source, Scenario.DocumentWrite, expectedCopies, cancellationToken);

        return new();
    }

    private async Task<OperationResult> UpdateAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        using var response = await OpenSearchHttp.SendMutationAsync(client, HttpMethod.Post,
            OpenSearchNames.PathSeparator + index + OpenSearchNames.UpdateDocumentPath + Uri.EscapeDataString(document.Id) + OpenSearchNames.UpdateQuery,
            OpenSearchDocument.CreateUpdate(document.Id, document.Json), Scenario.DocumentUpdate, expectedCopies, cancellationToken);
        return new();
    }

    private async Task<OperationResult> DeleteAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        using var response = await OpenSearchHttp.SendMutationAsync(client, HttpMethod.Delete,
            OpenSearchNames.PathSeparator + index + OpenSearchNames.DocumentPath + Uri.EscapeDataString(document.Id) + OpenSearchNames.CreateIndexSuffix,
            null, Scenario.DocumentDelete, expectedCopies, cancellationToken);
        return new();
    }

    private async Task<OperationResult> SearchAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        var request = OpenSearchVectorQuery.Create(document.Vector, topK);
        using var result = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Post, OpenSearchNames.PathSeparator + index + OpenSearchNames.SearchSuffix,
            request, cancellationToken);
        return new(Neighbors: OpenSearchSearchResponse.Read(result.RootElement, topK));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
