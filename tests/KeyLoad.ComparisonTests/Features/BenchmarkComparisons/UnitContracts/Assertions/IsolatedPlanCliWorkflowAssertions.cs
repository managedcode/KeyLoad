using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedPlanCliWorkflowAssertions
{
    private const int GitHubJobOutputBytes = 1_048_576;

    internal static async Task<IsolatedPlanCliArtifacts> VerifyPlanArtifactsAsync(string planPath, string scalePath,
        string vectorPath, string compositePath, string openLoopPath, string cliOutput, CancellationToken token)
    {
        var plan = JsonNode.Parse(cliOutput)!;
        await Assert.That(JsonNode.DeepEquals(plan, JsonNode.Parse(await File.ReadAllTextAsync(planPath, token)))).IsTrue();
        var canonical = (await IsolatedPlanNodeProcess.ProbeAsync("create"))[IsolatedPlanFields.Value]!;
        await Assert.That(JsonNode.DeepEquals(plan, canonical)).IsTrue();
        var scales = JsonNode.Parse(await File.ReadAllTextAsync(scalePath, token))!.AsArray();
        var expectedScales = (await IsolatedPlanNodeProcess.ProbeAsync("create-scales"))[IsolatedPlanFields.Value]!;
        await Assert.That(JsonNode.DeepEquals(scales, expectedScales)).IsTrue();
        var vectors = JsonNode.Parse(await File.ReadAllTextAsync(vectorPath, token))!.AsArray();
        var expectedVectors = (await IsolatedPlanNodeProcess.ProbeAsync("create-vectors"))[IsolatedPlanFields.Value]!;
        await Assert.That(JsonNode.DeepEquals(vectors, expectedVectors)).IsTrue();
        await VerifyCompositeAsync(compositePath, plan, scales, vectors, token);
        var openLoop = JsonNode.Parse(await File.ReadAllTextAsync(openLoopPath, token))!;
        var expectedOpenLoop = (await IsolatedPlanNodeProcess.ProbeAsync("create-open-loop"))[IsolatedPlanFields.Value]!;
        await Assert.That(JsonNode.DeepEquals(openLoop, expectedOpenLoop)).IsTrue();
        return new(planPath, scalePath, vectorPath, compositePath, openLoopPath, plan, scales, vectors, openLoop);
    }

    private static async Task VerifyCompositeAsync(string path, JsonNode plan, JsonArray scales, JsonArray vectors,
        CancellationToken token)
    {
        var composite = JsonNode.Parse(await File.ReadAllTextAsync(path, token))!;
        await Assert.That(composite[IsolatedPlanFields.SchemaVersion]!.GetValue<int>()).IsEqualTo(3);
        await Assert.That(JsonNode.DeepEquals(composite[IsolatedPlanFields.Control], plan)).IsTrue();
        await Assert.That(JsonNode.DeepEquals(composite[IsolatedPlanFields.ScaledProfiles], scales)).IsTrue();
        await Assert.That(JsonNode.DeepEquals(composite[IsolatedPlanFields.VectorProfiles], vectors)).IsTrue();
    }

    internal static async Task VerifyMatrixOutputAsync(string outputPath, IsolatedPlanCliArtifacts artifacts,
        IOptions<OpenLoopPlanProcessOptions> executionOptions, CancellationToken token)
    {
        var lines = await File.ReadAllLinesAsync(outputPath, token);
        await Assert.That(lines.Length).IsEqualTo(2);
        await Assert.That(lines[0]).IsEqualTo("sentinel=preserved");
        var fullOutput = await File.ReadAllTextAsync(outputPath, token);
        await Assert.That(Encoding.Unicode.GetByteCount(fullOutput)).IsLessThan(GitHubJobOutputBytes);
        const string prefix = "database_matrices=";
        await Assert.That(lines[1].StartsWith(prefix, StringComparison.Ordinal)).IsTrue();
        var matrices = JsonNode.Parse(lines[1][prefix.Length..])!.AsObject();
        var expected = (await IsolatedPlanNodeProcess.ProbeAsync("workflow-matrices", artifacts.Plan,
            scaledPlans: artifacts.Scales, vectorPlans: artifacts.Vectors, openLoopPlan: artifacts.OpenLoop))
            [IsolatedPlanFields.Value]!.AsObject();
        await IsolatedDatabaseMatrixAssertions.VerifyWorkflowProjectionAsync(matrices, expected, includeOpenLoop: true);
        await IsolatedPlanResolverAssertions.VerifySixRowKindsAsync(outputPath, artifacts, matrices, executionOptions, token);
    }
}
