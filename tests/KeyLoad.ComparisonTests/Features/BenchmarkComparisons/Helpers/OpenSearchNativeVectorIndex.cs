using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class OpenSearchNativeVectorIndex(Uri endpoint, int nodeCount) : IAsyncDisposable
{
    private const int Dimensions = 2;
    private readonly IOptions<NativeComparisonExecutionOptions> executionOptions = NativeExecutionPolicyFixture.Read();
    private static TimeSpan RequestTimeout => NativeExecutionPolicyFixture.Harness().Value.OpenSearchRequestTimeout;
    private static TimeSpan CleanupTimeout => NativeExecutionPolicyFixture.Harness().Value.OpenSearchIndexCleanupTimeout;
    private readonly HttpClient client = new() { BaseAddress = endpoint, Timeout = RequestTimeout };
    private readonly string index = OpenSearchNames.IndexNamePrefix + Guid.NewGuid().ToString(OpenSearchNames.GuidFormat);
    private bool owned;

    internal async Task CreateAsync(CancellationToken token)
    {
        owned = true;
        await OpenSearchIndex.CreateAsync(client, index, Dimensions, nodeCount - 1, token);
        _ = await OpenSearchClusterEvidence.ObserveAsync(client, index, nodeCount, OpenSearchNativeVectorRegression.Topology(nodeCount), executionOptions, token);
    }

    internal async Task WriteAsync(string id, object source, CancellationToken token)
    {
        using var result = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Put,
            OpenSearchNames.PathSeparator + index + OpenSearchNames.CreateDocumentPath + Uri.EscapeDataString(id) + OpenSearchNames.CreateIndexSuffix,
            source, token);
        OpenSearchWriteAcknowledgement.Verify(result.RootElement, nodeCount);
    }

    internal async Task RefreshAsync(CancellationToken token)
    {
        using var response = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Post,
            OpenSearchNames.PathSeparator + index + OpenSearchNames.RefreshSuffix, null, token);
    }

    internal async Task<ImmutableArray<FoundDocument>> SearchAsync(ImmutableArray<float> vector, int topK, CancellationToken token)
    {
        using var response = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Post,
            OpenSearchNames.PathSeparator + index + OpenSearchNames.SearchSuffix, OpenSearchVectorQuery.Create(vector, topK), token);
        return OpenSearchSearchResponse.Read(response.RootElement, topK);
    }

    internal async Task<JsonDocument> RawSearchAsync(ImmutableArray<float> vector, CancellationToken token)
        => await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Post, OpenSearchNames.PathSeparator + index + OpenSearchNames.SearchSuffix,
            OpenSearchVectorQuery.Create(vector, 1), token);

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (owned)
            {
                using var cleanup = new CancellationTokenSource(CleanupTimeout, TimeProvider.System);
                using var deleted = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Delete,
                    OpenSearchNames.PathSeparator + index, null, cleanup.Token, allowNotFound: true);
                using var absent = await OpenSearchHttp.SendJsonAsync(client, HttpMethod.Get,
                    OpenSearchNames.PathSeparator + index, null, cleanup.Token, allowNotFound: true);
                await Assert.That(absent.RootElement.EnumerateObject().Any()).IsFalse();
            }
        }
        finally
        {
            client.Dispose();
        }
    }
}
