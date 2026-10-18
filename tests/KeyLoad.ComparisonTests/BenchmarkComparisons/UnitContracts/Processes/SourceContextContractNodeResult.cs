using System.Text.Json;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed record SourceContextContractNodeResult(bool Succeeded, JsonElement Value, string Message);
