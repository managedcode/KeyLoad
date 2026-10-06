using System.Text;
using System.Text.Json.Nodes;
using KeyLoad.UnitTests.Features.RepositoryGovernance;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedPlanResolverAssertions
{
    private const string Sentinel = "sentinel=preserved\n";
    private const string FailureMessage = "Isolated GitHub evidence rejected.";
    private const string TamperedIdSuffix = "-tampered";
    private const string OtherTarget = "Qdrant";
    private const string WorkerKind = "worker";
    private const string PreflightEnvironment = Sentinel + "KEYLOAD_SCALE_PROFILE=\nKEYLOAD_VECTOR_PROFILE=\n"
        + "KEYLOAD_OPEN_LOOP_RATE=\nKEYLOAD_OPEN_LOOP_CANCELLATION_PROOF=\nBenchmarks__NodeCount=1\n"
        + "Benchmarks__Scenario=PointRead\nBenchmarks__EvidenceProfile=intensive-1k-c16\nBenchmarks__VectorProfile=\n";
    private const string ControlEnvironment = Sentinel + "KEYLOAD_SCALE_PROFILE=\nKEYLOAD_VECTOR_PROFILE=\n"
        + "KEYLOAD_OPEN_LOOP_RATE=\nKEYLOAD_OPEN_LOOP_CANCELLATION_PROOF=\nBenchmarks__NodeCount=3\n"
        + "Benchmarks__Scenario=PointRead\nBenchmarks__EvidenceProfile=intensive-1k-c16\nBenchmarks__VectorProfile=\n";
    private const string ScaledEnvironment = Sentinel + "KEYLOAD_SCALE_PROFILE=scaled-100k-c16\nKEYLOAD_VECTOR_PROFILE=\n"
        + "KEYLOAD_OPEN_LOOP_RATE=\nKEYLOAD_OPEN_LOOP_CANCELLATION_PROOF=\nBenchmarks__NodeCount=3\n"
        + "Benchmarks__Scenario=PointRead\nBenchmarks__EvidenceProfile=scaled-100k-c16\nBenchmarks__VectorProfile=\n";
    private const string VectorEnvironment = Sentinel + "KEYLOAD_SCALE_PROFILE=\n"
        + "KEYLOAD_VECTOR_PROFILE=vector-100k-exact-plain-c16\nKEYLOAD_OPEN_LOOP_RATE=\n"
        + "KEYLOAD_OPEN_LOOP_CANCELLATION_PROOF=\nBenchmarks__NodeCount=3\nBenchmarks__Scenario=VectorExact\n"
        + "Benchmarks__EvidenceProfile=vector-100k-exact-plain-c16\n"
        + "Benchmarks__VectorProfile=vector-100k-exact-plain-c16\n";
    private const string OpenLoopEnvironment = Sentinel + "KEYLOAD_SCALE_PROFILE=scaled-100k-c16\nKEYLOAD_VECTOR_PROFILE=\n"
        + "KEYLOAD_OPEN_LOOP_RATE=250\nKEYLOAD_OPEN_LOOP_CANCELLATION_PROOF=false\nBenchmarks__NodeCount=3\n"
        + "Benchmarks__Scenario=PointRead\nBenchmarks__EvidenceProfile=scaled-100k-c16\nBenchmarks__VectorProfile=\n";
    private const string ProofEnvironment = Sentinel + "KEYLOAD_SCALE_PROFILE=scaled-100k-c16\nKEYLOAD_VECTOR_PROFILE=\n"
        + "KEYLOAD_OPEN_LOOP_RATE=250\nKEYLOAD_OPEN_LOOP_CANCELLATION_PROOF=true\nBenchmarks__NodeCount=3\n"
        + "Benchmarks__Scenario=PointRead\nBenchmarks__EvidenceProfile=scaled-100k-c16\nBenchmarks__VectorProfile=\n";
    private static readonly string[] ResolverNames = ["isolated-plan.json", "scaled-plan.json", "vector-plan.json",
        "composite-plan.json", "open-loop-plan.json"];

    internal static async Task VerifySixRowKindsAsync(string outputPath, IsolatedPlanCliArtifacts artifacts,
        JsonObject projectedMatrices, IOptions<OpenLoopPlanProcessOptions> executionOptions, CancellationToken token)
    {
        var originalMatrixOutput = await File.ReadAllBytesAsync(outputPath, token);
        var directory = Path.Combine(Path.GetDirectoryName(outputPath)!, "comparison-plan");
        var originals = await CopyPlanFilesAsync(directory, artifacts, token);
        var rows = await ResolveKeyLoadRowsAsync(directory, projectedMatrices, executionOptions, token);
        var samples = SelectSamples(rows);
        for (var index = 0; index < samples.Length - 1; index++)
        {
            var sample = samples[index];
            await VerifySuccessfulResolutionAsync(executionOptions, directory, outputPath, sample.Name,
                sample.Row, sample.Kind, sample.ExpectedEnvironment, token);
        }
        await VerifyRejectedSelectorsAndCorrectedProofAsync(executionOptions, directory, outputPath,
            samples[^1].Row, samples[^1].Kind, samples[^1].ExpectedEnvironment, token);
        await VerifyPlanBytesUnchangedAsync(artifacts, directory, originals, outputPath, originalMatrixOutput, token);
    }

    private static async Task<byte[][]> CopyPlanFilesAsync(string directory, IsolatedPlanCliArtifacts artifacts,
        CancellationToken token)
    {
        Directory.CreateDirectory(directory);
        var sources = SourcePaths(artifacts);
        var originals = new byte[sources.Length][];
        for (var index = 0; index < sources.Length; index++)
        {
            originals[index] = await File.ReadAllBytesAsync(sources[index], token);
            var destination = Path.Combine(directory, ResolverNames[index]);
            await File.WriteAllBytesAsync(destination, originals[index], token);
            await AssertBytesEqualAsync(originals[index], await File.ReadAllBytesAsync(destination, token));
        }
        return originals;
    }

    private static async Task<JsonArray> ResolveKeyLoadRowsAsync(string directory, JsonObject projectedMatrices,
        IOptions<OpenLoopPlanProcessOptions> executionOptions, CancellationToken token)
    {
        var keyLoad = WorkflowDatabaseGroups.Entries.Single(static entry => entry.Name == "KeyLoad").Key;
        var result = await OpenLoopPlanJoinNodeProcess.ResolveMatrixGroupAsync(executionOptions,
            Path.Combine(directory, ResolverNames[0]), Path.Combine(directory, ResolverNames[1]),
            Path.Combine(directory, ResolverNames[2]), Path.Combine(directory, ResolverNames[4]), keyLoad,
            projectedMatrices[keyLoad]!.ToJsonString(), token);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(result.Error).IsEqualTo(string.Empty);
        var resolved = JsonNode.Parse(result.Output)!.AsObject();
        await Assert.That(resolved[IsolatedPlanFields.Rejected]!.GetValue<bool>()).IsFalse();
        var rows = resolved[IsolatedPlanFields.Include]!.AsArray();
        await Assert.That(rows.Count).IsEqualTo(207);
        await Assert.That(rows.Count(static row => row![IsolatedPlanFields.Preflight]!.GetValue<bool>())).IsEqualTo(3);
        await Assert.That(rows.Count(static row => !row![IsolatedPlanFields.Preflight]!.GetValue<bool>()
            && row[IsolatedPlanFields.Profile]!.GetValue<string>() == "intensive-1k-c16")).IsEqualTo(30);
        await Assert.That(rows.Count(static row => row![IsolatedPlanFields.OpenLoopRate] is not null)).IsEqualTo(78);
        return rows;
    }

    private static (string Name, JsonObject Row, string Kind, string ExpectedEnvironment)[] SelectSamples(JsonArray rows)
    {
        var records = rows.Select(static row => row!.AsObject()).ToArray();
        return
        [
            ("preflight", Single(records, static row => row[IsolatedPlanFields.Preflight]!.GetValue<bool>()
                && row[IsolatedPlanFields.NodeCount]!.GetValue<int>() == 1), "preflight", PreflightEnvironment),
            ("control", Single(records, static row => IsOriginalWorker(row)
                && IsProfile(row, "intensive-1k-c16") && IsPointRead(row)), WorkerKind, ControlEnvironment),
            ("scaled", Single(records, static row => IsOriginalWorker(row)
                && IsProfile(row, "scaled-100k-c16") && IsPointRead(row)), WorkerKind, ScaledEnvironment),
            ("vector", Single(records, static row => IsProfile(row, "vector-100k-exact-plain-c16")
                && row[IsolatedPlanFields.NodeCount]!.GetValue<int>() == 3
                && row[IsolatedPlanFields.Scenario]!.GetValue<string>() == "VectorExact"), WorkerKind, VectorEnvironment),
            ("open-loop", Single(records, static row => row[IsolatedPlanFields.OpenLoopRate]?.GetValue<int>() == 250
                && row[IsolatedPlanFields.OpenLoopCancellationProof]?.GetValue<bool>() == false
                && IsProfile(row, "scaled-100k-c16") && IsPointRead(row)), "open-loop", OpenLoopEnvironment),
            ("proof", Single(records, static row => row[IsolatedPlanFields.OpenLoopRate]?.GetValue<int>() == 250
                && row[IsolatedPlanFields.OpenLoopCancellationProof]?.GetValue<bool>() == true
                && IsProfile(row, "scaled-100k-c16") && IsPointRead(row)), "proof", ProofEnvironment)
        ];
    }

    private static async Task VerifySuccessfulResolutionAsync(IOptions<OpenLoopPlanProcessOptions> executionOptions,
        string planDirectory, string outputPath, string name, JsonObject row, string kind,
        string expectedEnvironment, CancellationToken token)
    {
        var environmentPath = Path.Combine(Path.GetDirectoryName(outputPath)!, "resolved-" + name + ".txt");
        await File.WriteAllTextAsync(environmentPath, Sentinel, token);
        var original = await File.ReadAllBytesAsync(environmentPath, token);
        await AssertBytesEqualAsync(Encoding.UTF8.GetBytes(Sentinel), original);
        var result = await ResolveAsync(executionOptions, planDirectory, environmentPath, row, kind, token);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(result.Error).IsEqualTo(string.Empty);
        await AssertBytesEqualAsync(Encoding.UTF8.GetBytes(expectedEnvironment), await File.ReadAllBytesAsync(environmentPath, token));
    }

    private static async Task VerifyRejectedSelectorsAndCorrectedProofAsync(
        IOptions<OpenLoopPlanProcessOptions> executionOptions, string planDirectory, string outputPath,
        JsonObject proof, string kind, string expectedEnvironment, CancellationToken token)
    {
        var environmentPath = Path.Combine(Path.GetDirectoryName(outputPath)!, "resolved-rejections.txt");
        await File.WriteAllTextAsync(environmentPath, Sentinel, token);
        var original = await File.ReadAllBytesAsync(environmentPath, token);
        await RejectAsync(executionOptions, planDirectory, environmentPath, proof, OtherTarget,
            proof[IsolatedPlanFields.Id]!.GetValue<string>(), kind, original, token);
        await RejectAsync(executionOptions, planDirectory, environmentPath, proof,
            proof[IsolatedPlanFields.Target]!.GetValue<string>(), proof[IsolatedPlanFields.Id]!.GetValue<string>() + TamperedIdSuffix,
            kind, original, token);
        await RejectAsync(executionOptions, planDirectory, environmentPath, proof,
            proof[IsolatedPlanFields.Target]!.GetValue<string>(), proof[IsolatedPlanFields.Id]!.GetValue<string>(),
            WorkerKind, original, token);
        var corrected = await ResolveAsync(executionOptions, planDirectory, environmentPath, proof, kind, token);
        await Assert.That(corrected.ExitCode).IsEqualTo(0).Because(corrected.Error);
        await Assert.That(corrected.Error).IsEqualTo(string.Empty);
        await AssertBytesEqualAsync(Encoding.UTF8.GetBytes(expectedEnvironment), await File.ReadAllBytesAsync(environmentPath, token));
    }

    private static async Task RejectAsync(IOptions<OpenLoopPlanProcessOptions> executionOptions, string planDirectory,
        string environmentPath, JsonObject row, string target, string id, string kind, byte[] original,
        CancellationToken token)
    {
        var result = await OpenLoopPlanJoinNodeProcess.ResolveMatrixEntryAsync(executionOptions, planDirectory,
            environmentPath, target, id, row[IsolatedPlanFields.JobName]!.GetValue<string>(), kind, token);
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.Error.Trim()).IsEqualTo(FailureMessage);
        await Assert.That(result.Output).IsEqualTo(string.Empty);
        await AssertBytesEqualAsync(original, await File.ReadAllBytesAsync(environmentPath, token));
    }

    private static Task<OpenLoopPlanNodeResult> ResolveAsync(IOptions<OpenLoopPlanProcessOptions> executionOptions,
        string planDirectory, string environmentPath, JsonObject row, string kind, CancellationToken token)
        => OpenLoopPlanJoinNodeProcess.ResolveMatrixEntryAsync(executionOptions, planDirectory, environmentPath,
            row[IsolatedPlanFields.Target]!.GetValue<string>(), row[IsolatedPlanFields.Id]!.GetValue<string>(),
            row[IsolatedPlanFields.JobName]!.GetValue<string>(), kind, token);

    private static async Task VerifyPlanBytesUnchangedAsync(IsolatedPlanCliArtifacts artifacts, string directory,
        byte[][] originals, string outputPath, byte[] originalMatrixOutput, CancellationToken token)
    {
        var sources = SourcePaths(artifacts);
        for (var index = 0; index < sources.Length; index++)
        {
            await AssertBytesEqualAsync(originals[index], await File.ReadAllBytesAsync(sources[index], token));
            await AssertBytesEqualAsync(originals[index], await File.ReadAllBytesAsync(
                Path.Combine(directory, ResolverNames[index]), token));
        }
        await AssertBytesEqualAsync(originalMatrixOutput, await File.ReadAllBytesAsync(outputPath, token));
    }

    private static string[] SourcePaths(IsolatedPlanCliArtifacts artifacts)
        => [artifacts.PlanPath, artifacts.ScalePath, artifacts.VectorPath, artifacts.CompositePath, artifacts.OpenLoopPath];

    private static bool IsProfile(JsonObject row, string profile)
        => row[IsolatedPlanFields.Profile]!.GetValue<string>() == profile;

    private static bool IsOriginalWorker(JsonObject row)
        => !row[IsolatedPlanFields.Preflight]!.GetValue<bool>() && row[IsolatedPlanFields.OpenLoopRate] is null;

    private static bool IsPointRead(JsonObject row)
        => row[IsolatedPlanFields.NodeCount]!.GetValue<int>() == 3
            && row[IsolatedPlanFields.Scenario]!.GetValue<string>() == "PointRead";

    private static JsonObject Single(IEnumerable<JsonObject> rows, Func<JsonObject, bool> predicate)
        => rows.Single(predicate);

    private static async Task AssertBytesEqualAsync(byte[] expected, byte[] actual)
        => await Assert.That(actual).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
}
