using System.Globalization;
using System.Text.Json.Nodes;
using TUnit.Assertions.Enums;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

/// <summary>Merges the result files, builds the real site and reads the comparison table rendered for each data type.</summary>
internal sealed class SiteResultsTableTests
{
    private const string Graph = "graph";
    private const string Traverse = "traverse";

    [Test]
    public async Task EveryTypeComparesKeyLoadWithItsDatabasesOverTheTypeCases()
    {
        var page = await BuildAsync(TestContext.Current!.Execution.CancellationToken);

        foreach (var type in SiteResultType.All)
        {
            var section = page.Section(type.Id);
            await Assert.That(SiteResultsPage.Title(section)).IsEqualTo(type.Label);
            await Assert.That(SiteResultsPage.Columns(section)).IsEquivalentTo(type.Cases, CollectionOrdering.Matching);
            var rows = SiteResultsPage.Rows(section);
            await Assert.That(rows.Select(row => row.Database)).IsEquivalentTo(type.Databases, CollectionOrdering.Matching);
            await Assert.That(rows.All(row => row.Cells.Keys.SequenceEqual(type.Cases))).IsTrue();
        }
    }

    [Test]
    public async Task EveryPassedCaseShowsItsRecordedValue()
    {
        var page = await BuildAsync(TestContext.Current!.Execution.CancellationToken);

        foreach (var result in SiteResultFiles.Artifacts.Select(SiteResultFiles.Read))
        {
            var row = SiteResultsPage.Row(page.Section((string)result[SiteTokens.Type]!), (string)result[SiteTokens.Database]!);
            var nodes = (int)result[SiteTokens.Nodes]!;
            foreach (var item in result[SiteTokens.Cases]!.AsArray().Select(node => node!.AsObject())
                .Where(item => (string)item[SiteTokens.Status]! == SiteTokens.Passed))
            {
                var id = (string)item[SiteTokens.Id]!;
                var line = row.Cells[id].Single(entry => entry.Nodes == nodes);
                var field = SiteResultType.TimedCases.Contains(id) ? SiteTokens.Seconds : SiteTokens.OpsPerSecond;
                await Assert.That(line.Status).IsEqualTo(SiteTokens.Passed);
                await Assert.That(double.Parse(line.Value, CultureInfo.InvariantCulture)).IsEqualTo((double)item[field]!);
            }
        }
    }

    [Test]
    public async Task FailedCaseIsShownAsFailedWithItsErrorAndRecordedCounts()
    {
        var page = await BuildAsync(TestContext.Current!.Execution.CancellationToken);
        var section = page.Section(Graph);
        var source = SiteResultFiles.Read("benchmark-keyload-graph-n1")[SiteTokens.Cases]![2]!.AsObject();

        var traverse = SiteResultsPage.Row(section, SiteTokens.KeyLoad).Cells[Traverse];
        var failed = traverse.Single(line => line.Nodes == 1);
        await Assert.That(failed.Status).IsEqualTo(SiteTokens.Failed);
        await Assert.That(failed.Value).IsEmpty();
        await Assert.That(failed.Text)
            .IsEqualTo(SiteTokens.FailedText + "TimeoutException: Traverse timed out after the server closed the connection.");
        await Assert.That(traverse.Single(line => line.Nodes == 3).Value).IsEqualTo("1250");
        var recorded = SiteResultsPage.Recorded(section, SiteTokens.KeyLoad, 1, Traverse);
        await Assert.That(SiteResultsPage.Attribute(recorded, "data-status")).IsEqualTo(SiteTokens.Failed);
        foreach (var field in new[] { SiteTokens.Operations, SiteTokens.Errors, SiteTokens.OpsPerSecond })
        {
            await Assert.That(double.Parse(SiteResultsPage.Attribute(SiteResultsPage.Field(recorded, field), "data-value"),
                CultureInfo.InvariantCulture)).IsEqualTo((double)source[field]!);
        }

        await Assert.That(SiteResultsPage.ErrorText(recorded)).IsEqualTo((string)source[SiteTokens.Error]!);
    }

    [Test]
    public async Task JobThatFailedBeforeItsCasesShowsItsErrorInEveryCase()
    {
        var page = await BuildAsync(TestContext.Current!.Execution.CancellationToken);
        var section = page.Section(Graph);
        var error = (string)SiteResultFiles.Read("benchmark-surrealdb-graph-n1")[SiteTokens.Error]!;

        var surreal = SiteResultsPage.Row(section, "SurrealDB");
        await Assert.That(surreal.Cells.Values.All(lines => lines.Length == 1 && lines[0].Status == SiteTokens.Failed
            && lines[0].Text == SiteTokens.FailedText + error)).IsTrue();
        foreach (var caseId in SiteResultType.Find(Graph).Cases)
        {
            var recorded = SiteResultsPage.Recorded(section, "SurrealDB", 1, caseId);
            await Assert.That(SiteResultsPage.Attribute(recorded, "data-status")).IsEqualTo(SiteTokens.Failed);
            await Assert.That(SiteResultsPage.ErrorText(recorded)).IsEqualTo(error);
        }
    }

    [Test]
    public async Task DatabaseWithoutAThreeNodeResultIsShownAsSingleNode()
    {
        var page = await BuildAsync(TestContext.Current!.Execution.CancellationToken);
        var graph = page.Section(Graph);

        var neo4j = SiteResultsPage.Row(graph, "Neo4j");
        await Assert.That(neo4j.Note).IsEqualTo(SiteTokens.SingleNode);
        await Assert.That(neo4j.Cells.Values.All(lines => lines.Length == 1 && lines[0].Nodes == 1)).IsTrue();
        var keyLoad = SiteResultsPage.Row(graph, SiteTokens.KeyLoad);
        await Assert.That(keyLoad.Note).IsEmpty();
        await Assert.That(keyLoad.Cells.Values.All(lines => lines.Select(line => line.Nodes).SequenceEqual([1, 3]))).IsTrue();
        await Assert.That(keyLoad.Cells[SiteTokens.Load].Single(line => line.Nodes == 3).Text).IsEqualTo("6,667−20%");
        await Assert.That(SiteResultsPage.Row(page.Section("key-value"), "Redis").Note).IsEqualTo(SiteTokens.SingleNode);
    }

    [Test]
    public async Task DatabaseWithoutAnyResultShowsNoData()
    {
        var page = await BuildAsync(TestContext.Current!.Execution.CancellationToken);

        foreach (var (type, database) in new[] { (Graph, "HelixDB"), ("search", "OpenSearch"), ("events", "KurrentDB") })
        {
            var row = SiteResultsPage.Row(page.Section(type), database);
            await Assert.That(row.Note).IsEqualTo(SiteTokens.NoResult);
            var lines = row.Cells.Values.SelectMany(cell => cell).ToArray();
            await Assert.That(lines.All(line => line.Status == SiteTokens.Missing && line.Value.Length == 0)).IsTrue();
            await Assert.That(lines.Count(line => line.Text.StartsWith(SiteTokens.NoData, StringComparison.Ordinal)))
                .IsEqualTo(row.Cells.Count);
        }
    }

    [Test]
    public async Task BuildPublishesTheMergedFileUnchanged()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var workspace = SiteWorkspace.Create();
        await MergeAndBuildAsync(workspace, token);

        var published = await File.ReadAllBytesAsync(Path.Combine(workspace.Site, SiteTokens.DataDirectory, SiteTokens.MergedFile), token);
        var merged = await File.ReadAllBytesAsync(workspace.Merged, token);

        await Assert.That(published.AsSpan().SequenceEqual(merged)).IsTrue();
    }

    [Test]
    public async Task BuildWithoutResultsStatesThatNoResultsExistYet()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var workspace = SiteWorkspace.Create();

        var build = await workspace.BuildAsync(SiteTokens.NoResults, token);

        await Assert.That(build.ExitCode).IsEqualTo(SiteTokens.Success);
        var page = await SiteResultsPage.ReadAsync(workspace.Site, token);
        await Assert.That(page.Html).Contains(SiteTokens.EmptyState);
        await Assert.That(page.Html).DoesNotContain(SiteTokens.ResultsContainer);
        await Assert.That(Directory.Exists(Path.Combine(workspace.Site, SiteTokens.DataDirectory))).IsFalse();
    }

    [Test]
    public async Task BuildRejectsAMergedFileOutsideTheContract()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var workspace = SiteWorkspace.Create();
        var result = SiteResultFiles.Read("benchmark-keyload-queue-n1");
        result[SiteTokens.Cases]![0]![SiteTokens.Status] = "skipped";
        var merged = Path.Combine(workspace.Path, SiteTokens.MergedFile);
        await File.WriteAllTextAsync(merged, new JsonObject { [SiteTokens.Results] = new JsonArray(result) }.ToJsonString(), token);

        var build = await workspace.BuildAsync(merged, token);

        await Assert.That(build.ExitCode).IsEqualTo(SiteTokens.Failure);
        await Assert.That(build.StandardError).Contains("invalid case cycle status.");
        await Assert.That(Directory.Exists(workspace.Site)).IsFalse();
    }

    private static async Task<SiteResultsPage> BuildAsync(CancellationToken cancellationToken)
    {
        await using var workspace = SiteWorkspace.Create();
        await MergeAndBuildAsync(workspace, cancellationToken);
        return await SiteResultsPage.ReadAsync(workspace.Site, cancellationToken);
    }

    private static async Task MergeAndBuildAsync(SiteWorkspace workspace, CancellationToken cancellationToken)
    {
        await workspace.CopyArtifactsAsync(SiteResultFiles.Artifacts, cancellationToken);
        var merge = await workspace.MergeAsync(cancellationToken);
        await Assert.That(merge.ExitCode).IsEqualTo(SiteTokens.Success);
        var build = await workspace.BuildAsync(workspace.Merged, cancellationToken);
        await Assert.That(build.StandardError).IsEmpty();
        await Assert.That(build.ExitCode).IsEqualTo(SiteTokens.Success);
    }
}
