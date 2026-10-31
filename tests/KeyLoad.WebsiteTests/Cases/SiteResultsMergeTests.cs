using System.Text.Json.Nodes;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

/// <summary>Runs the real merge script over result files laid out like downloaded Benchmarks job artifacts.</summary>
internal sealed class SiteResultsMergeTests
{
    private const string GraphArtifact = "benchmark-keyload-graph-n1";
    private const string GraphFile = "keyload-graph-n1.json";
    private const string UnknownField = "host";

    [Test]
    public async Task MergeKeepsEveryResultFileInTypeDatabaseAndNodeOrder()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var workspace = SiteWorkspace.Create();
        await workspace.CopyArtifactsAsync(SiteResultFiles.Artifacts, token);

        var merge = await workspace.MergeAsync(token);

        await Assert.That(merge.ExitCode).IsEqualTo(SiteTokens.Success);
        await Assert.That(merge.StandardError).IsEmpty();
        var merged = JsonNode.Parse(await File.ReadAllTextAsync(workspace.Merged, token))!.AsObject();
        await Assert.That(merged.Select(property => property.Key)).IsEquivalentTo(new[] { SiteTokens.Results });
        var results = merged[SiteTokens.Results]!.AsArray();
        var expected = SiteResultFiles.Artifacts.Select(SiteResultFiles.Read).OrderBy(TypeOrder).ThenBy(DatabaseOrder)
            .ThenBy(result => (int)result[SiteTokens.Nodes]!).ToArray();
        await Assert.That(results.Count).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(JsonNode.DeepEquals(results[index], expected[index])).IsTrue();
        }
    }

    [Test]
    public async Task MergeFailsWhenTheArtifactsHoldNoResultFile()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var workspace = SiteWorkspace.Create();
        var logs = Path.Combine(workspace.Input, "benchmark-keyload-documents-n1", "test-results");
        Directory.CreateDirectory(logs);
        await File.WriteAllTextAsync(Path.Combine(logs, "KeyLoad.ComparisonHost.trx"), "<TestRun />", token);

        var merge = await workspace.MergeAsync(token);

        await Assert.That(merge.ExitCode).IsEqualTo(SiteTokens.Failure);
        await Assert.That(merge.StandardError).Contains("No benchmark result files were found");
        await Assert.That(File.Exists(workspace.Merged)).IsFalse();
    }

    [Test]
    [Arguments("unknown-case", "invalid case shortest-path id.")]
    [Arguments("passed-without-rate", "invalid case neighbors opsPerSecond.")]
    [Arguments("failed-without-error", "invalid case traverse error.")]
    [Arguments("zero-batch", "invalid case neighbors batchSize.")]
    [Arguments("unknown-database", "invalid database.")]
    [Arguments("extra-field", "invalid result fields.")]
    [Arguments("two-node-cluster", "invalid nodes.")]
    [Arguments("other-file-name", "the file name does not match keyload-graph-n1.json.")]
    public async Task MergeRejectsAResultOutsideTheContract(string change, string message)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var workspace = SiteWorkspace.Create();
        await workspace.CopyArtifactsAsync(["benchmark-neo4j-graph-n1"], token);
        var fileName = change == "other-file-name" ? "keyload-graph-n3.json" : GraphFile;
        await workspace.WriteResultAsync(GraphArtifact, fileName, Change(SiteResultFiles.Read(GraphArtifact), change), token);

        var merge = await workspace.MergeAsync(token);

        await Assert.That(merge.ExitCode).IsEqualTo(SiteTokens.Failure);
        await Assert.That(merge.StandardError).Contains($"{GraphArtifact}/{fileName}: {message}");
        await Assert.That(File.Exists(workspace.Merged)).IsFalse();
    }

    [Test]
    public async Task MergeRejectsTwoFilesForTheSameDatabaseTypeAndNodes()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var workspace = SiteWorkspace.Create();
        await workspace.CopyArtifactsAsync([GraphArtifact], token);
        await workspace.WriteResultAsync(GraphArtifact + "-attempt-2", GraphFile, SiteResultFiles.Read(GraphArtifact), token);

        var merge = await workspace.MergeAsync(token);

        await Assert.That(merge.ExitCode).IsEqualTo(SiteTokens.Failure);
        await Assert.That(merge.StandardError).Contains("repeats graph|KeyLoad|1");
        await Assert.That(File.Exists(workspace.Merged)).IsFalse();
    }

    private static JsonObject Change(JsonObject result, string change)
    {
        var cases = result[SiteTokens.Cases]!.AsArray();
        var neighbors = cases[1]!.AsObject();
        var traverse = cases[2]!.AsObject();
        switch (change)
        {
            case "unknown-case":
                traverse[SiteTokens.Id] = "shortest-path";
                break;
            case "passed-without-rate":
                neighbors[SiteTokens.OpsPerSecond] = null;
                break;
            case "failed-without-error":
                traverse[SiteTokens.Error] = null;
                break;
            case "zero-batch":
                neighbors[SiteTokens.BatchSize] = 0;
                break;
            case "unknown-database":
                result[SiteTokens.Database] = "Cassandra";
                break;
            case "extra-field":
                result[UnknownField] = "runner-7";
                break;
            case "two-node-cluster":
                result[SiteTokens.Nodes] = 2;
                break;
            case "other-file-name":
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown result change.");
        }

        return result;
    }

    private static int TypeOrder(JsonObject result)
        => Array.FindIndex(SiteResultType.All, type => type.Id == (string)result[SiteTokens.Type]!);

    private static int DatabaseOrder(JsonObject result)
        => Array.IndexOf(SiteResultType.Find((string)result[SiteTokens.Type]!).Databases, (string)result[SiteTokens.Database]!);
}
