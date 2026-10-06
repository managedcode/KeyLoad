using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed record IsolatedPlanCliArtifacts(string PlanPath, string ScalePath, string VectorPath,
    string CompositePath, string OpenLoopPath, JsonNode Plan, JsonArray Scales, JsonArray Vectors, JsonNode OpenLoop);
