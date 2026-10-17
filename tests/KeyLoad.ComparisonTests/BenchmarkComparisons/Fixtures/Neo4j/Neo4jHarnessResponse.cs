using System.Text.Json;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class Neo4jHarnessResponse(int statusCode, JsonDocument document) : IDisposable
{
    public int StatusCode { get; } = statusCode;
    public JsonDocument Document { get; } = document;

    public void Dispose() => Document.Dispose();
}
