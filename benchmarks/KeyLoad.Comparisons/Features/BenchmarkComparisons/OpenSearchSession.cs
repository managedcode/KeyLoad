using System.Runtime.InteropServices;

namespace KeyLoad.Comparisons.Targets;

internal sealed class OpenSearchSession(HttpClient client, string index, int topK, int expectedCopies) : IComparisonSession
{
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
            Scenario.VectorExact => await SearchAsync(document, cancellationToken),
            _ => throw new NotSupportedException(OpenSearchNames.Unsupported)
        };
    }

    private async Task<OperationResult> WriteAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        var source = OpenSearchDocument.CreateWithoutVector(document.Id, document.Json);
        using var response = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Put,
            OpenSearchNames.PathSeparator + index + OpenSearchNames.CreateDocumentPath + Uri.EscapeDataString(document.Id) + OpenSearchNames.CreateIndexSuffix,
            source, cancellationToken);
        OpenSearchWriteAcknowledgement.Verify(response.RootElement, expectedCopies);
        if (OpenSearchJson.RequiredString(response.RootElement, OpenSearchNames.Result) != OpenSearchNames.Created)
        {
            throw new ComparisonFailureException(OpenSearchNames.BulkItemFailure);
        }

        return new();
    }

    private async Task<OperationResult> SearchAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        var request = OpenSearchVectorQuery.Create(document.Vector, topK);
        using var result = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Post, OpenSearchNames.PathSeparator + index + OpenSearchNames.SearchSuffix,
            request, cancellationToken);
        var root = result.RootElement;
        OpenSearchSearchResponse.Verify(root);
        var hits = root.GetProperty(OpenSearchNames.Hits).GetProperty(OpenSearchNames.Hits);
        return new(Neighbors: ImmutableCollectionsMarshal.AsImmutableArray(hits.EnumerateArray()
            .Select(hit => OpenSearchDocument.Read(hit.GetProperty(OpenSearchNames.Source))).ToArray()));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
