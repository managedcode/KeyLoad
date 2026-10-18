using System.Text.Json;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed record ImageToolingContractNodeResult(bool Succeeded, JsonElement Value, string Message);
