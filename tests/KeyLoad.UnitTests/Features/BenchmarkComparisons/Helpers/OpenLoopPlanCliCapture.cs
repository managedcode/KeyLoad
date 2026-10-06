using System.Text.Json.Nodes;
using KeyLoad.UnitTests.Features.RepositoryGovernance;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class OpenLoopPlanCliCapture
{
    private const string PlanFile = "plan.json";
    private const string ScaledFile = "scaled.json";
    private const string VectorFile = "vector.json";
    private const string CompositeFile = "composite.json";
    private const string GithubFile = "github.txt";
    private const string OpenLoopFile = "open-loop.json";
    private const string GithubSeed = "retained=sentinel\n";
    private const string MatrixPrefix = "database_matrices=";
    private const string OpenLoopArgument = "--open-loop-output=";

    internal static async Task<OpenLoopPlanCliSnapshot> CaptureAsync(IOptions<OpenLoopPlanProcessOptions> executionOptions,
        string directory, bool includeOpenLoop, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var planPath = Path.Combine(directory, PlanFile);
        var scaledPath = Path.Combine(directory, ScaledFile);
        var vectorPath = Path.Combine(directory, VectorFile);
        var compositePath = Path.Combine(directory, CompositeFile);
        var githubPath = Path.Combine(directory, GithubFile);
        var openLoopPath = includeOpenLoop ? Path.Combine(directory, OpenLoopFile) : null;
        await File.WriteAllTextAsync(githubPath, GithubSeed, cancellationToken).ConfigureAwait(false);
        var arguments = Arguments(planPath, scaledPath, vectorPath, compositePath, githubPath, openLoopPath);
        var result = await OpenLoopPlanJoinNodeProcess.RunCliAsync(executionOptions, arguments, cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        var githubLines = await File.ReadAllLinesAsync(githubPath, cancellationToken).ConfigureAwait(false);
        var matrixLine = githubLines.Single(line => line.StartsWith(MatrixPrefix, StringComparison.Ordinal));
        var projectedMatrices = JsonNode.Parse(matrixLine[MatrixPrefix.Length..])!.AsObject();
        var matrices = await ResolveFullMatricesAsync(executionOptions, planPath, scaledPath, vectorPath,
            openLoopPath, projectedMatrices, cancellationToken).ConfigureAwait(false);
        return new(planPath, scaledPath, vectorPath, compositePath, githubPath, openLoopPath,
            await File.ReadAllBytesAsync(planPath, cancellationToken).ConfigureAwait(false),
            await File.ReadAllBytesAsync(scaledPath, cancellationToken).ConfigureAwait(false),
            await File.ReadAllBytesAsync(vectorPath, cancellationToken).ConfigureAwait(false),
            await File.ReadAllBytesAsync(compositePath, cancellationToken).ConfigureAwait(false),
            await File.ReadAllBytesAsync(githubPath, cancellationToken).ConfigureAwait(false), result.ExitCode, result.Output,
            result.Error, githubLines, JsonNode.Parse(await File.ReadAllTextAsync(planPath, cancellationToken)
                .ConfigureAwait(false))!.AsObject(), JsonNode.Parse(await File.ReadAllTextAsync(scaledPath, cancellationToken)
                .ConfigureAwait(false))!.AsArray(), JsonNode.Parse(await File.ReadAllTextAsync(vectorPath, cancellationToken)
                .ConfigureAwait(false))!.AsArray(), projectedMatrices, matrices);
    }

    private static async Task<JsonObject> ResolveFullMatricesAsync(
        IOptions<OpenLoopPlanProcessOptions> executionOptions, string planPath, string scaledPath,
        string vectorPath, string? openLoopPath, JsonObject projected, CancellationToken cancellationToken)
    {
        var full = new JsonObject();
        foreach (var (groupKey, _) in WorkflowDatabaseGroups.Entries)
        {
            var result = await OpenLoopPlanJoinNodeProcess.ResolveMatrixGroupAsync(executionOptions, planPath,
                scaledPath, vectorPath, openLoopPath, groupKey,
                projected[groupKey]!.ToJsonString(), cancellationToken).ConfigureAwait(false);
            await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
            await Assert.That(result.Error).IsEqualTo(string.Empty);
            var resolved = JsonNode.Parse(result.Output)!.AsObject();
            await Assert.That(resolved[IsolatedPlanFields.Rejected]!.GetValue<bool>()).IsFalse();
            full[groupKey] = new JsonObject { [IsolatedPlanFields.Include] = resolved[IsolatedPlanFields.Include]!.DeepClone() };
        }
        return full;
    }

    internal static string[] Arguments(string planPath, string scaledPath, string vectorPath, string compositePath,
        string githubPath, string? openLoopPath)
    {
        var arguments = new List<string>
        {
            "--output=" + planPath,
            "--scale-output=" + scaledPath,
            "--vector-output=" + vectorPath,
            "--composite-output=" + compositePath,
            "--github-output=" + githubPath,
        };
        if (openLoopPath is not null)
        {
            arguments.Add(OpenLoopArgument + openLoopPath);
        }
        return arguments.ToArray();
    }
}
