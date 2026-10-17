using System.Text.Json;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed record ImageToolingContractNodeResult(bool Succeeded, JsonElement Value, string Message);
