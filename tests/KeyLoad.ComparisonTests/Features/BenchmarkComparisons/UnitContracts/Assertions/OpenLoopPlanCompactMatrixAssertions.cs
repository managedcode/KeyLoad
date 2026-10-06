using System.Text;
using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class OpenLoopPlanCompactMatrixAssertions
{
    private const int GitHubJobOutputByteLimit = 1_048_576;
    private const string PreflightProperty = "preflight";
    private const string OpenLoopRateProperty = "openLoopRate";
    private const string OpenLoopProofProperty = "openLoopCancellationProof";
    private const string JobNameProperty = "jobName";

    internal static async Task VerifyAsync(OpenLoopPlanCliSnapshot snapshot)
    {
        var outputText = Encoding.UTF8.GetString(snapshot.GithubBytes);
        await Assert.That(Encoding.Unicode.GetByteCount(outputText)).IsLessThan(GitHubJobOutputByteLimit);
        var expected = new JsonObject();
        foreach (var (key, matrix) in snapshot.Matrices)
        {
            var rows = matrix![IsolatedPlanFields.Include]!.AsArray();
            var projected = rows.Select(ProjectRow).ToArray();
            expected[key] = new JsonObject { [IsolatedPlanFields.Include] = new JsonArray(projected) };
        }
        await IsolatedDatabaseMatrixAssertions.VerifyWorkflowProjectionAsync(snapshot.ProjectedMatrices, expected, includeOpenLoop: snapshot.OpenLoopPath is not null)
            .ConfigureAwait(false);
    }

    private static JsonObject ProjectRow(JsonNode? value)
    {
        var row = value!.AsObject();
        return new JsonObject
        {
            [IsolatedPlanFields.Id] = row[IsolatedPlanFields.Id]!.DeepClone(),
            [JobNameProperty] = row[JobNameProperty]!.DeepClone(),
            [IsolatedPlanFields.Target] = row[IsolatedPlanFields.Target]!.DeepClone(),
            [IsolatedPlanFields.Kind] = MatrixKind(row)
        };
    }

    private static string MatrixKind(JsonObject row)
    {
        if (row[PreflightProperty]!.GetValue<bool>())
        {
            return IsolatedPlanFields.MatrixPreflightKind;
        }
        if (row[OpenLoopProofProperty]?.GetValue<bool>() == true)
        {
            return IsolatedPlanFields.MatrixProofKind;
        }
        return row[OpenLoopRateProperty] is null ? IsolatedPlanFields.MatrixWorkerKind : IsolatedPlanFields.OpenLoop;
    }
}
