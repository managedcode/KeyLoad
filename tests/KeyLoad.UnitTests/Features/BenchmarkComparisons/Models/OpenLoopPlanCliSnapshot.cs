using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed record OpenLoopPlanCliSnapshot(
    string PlanPath,
    string ScaledPath,
    string VectorPath,
    string CompositePath,
    string GithubPath,
    string? OpenLoopPath,
    byte[] PlanBytes,
    byte[] ScaledBytes,
    byte[] VectorBytes,
    byte[] CompositeBytes,
    byte[] GithubBytes,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    string[] GithubLines,
    JsonObject Plan,
    JsonArray ScaledPlans,
    JsonArray VectorPlans,
    JsonObject Matrices);
