using System.Globalization;
using TUnit.Assertions.Enums;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

/// <summary>Checks the global comparison against the original operation counts and measured durations.</summary>
internal sealed class SiteResultsSummaryTests
{
    private const string Write = "write";
    private const string Read = "read";
    private const string Search = "search";
    private const string Mixed = "mixed";

    [Test]
    public async Task AllTypesShowsEachDatabaseExactlyOnceWithCommonMetrics()
    {
        var page = await BuildAsync(TestContext.Current!.Execution.CancellationToken);
        var section = page.Section();
        var rows = SiteResultsPage.Rows(section);
        var databases = SiteResultType.All.SelectMany(type => type.Databases).Distinct().ToArray();

        await Assert.That(SiteResultsPage.Title(section)).IsEqualTo("All types");
        await Assert.That(SiteResultsPage.Columns(section)).IsEquivalentTo(
            new[] { "load", "write", "read", "search", "delete", "mixed" }, CollectionOrdering.Matching);
        await Assert.That(rows.Select(row => row.Database)).IsEquivalentTo(databases, CollectionOrdering.Any);
        await Assert.That(rows.Length).IsEqualTo(databases.Length);
        await Assert.That(rows.Select(row => row.Database).Distinct().Count()).IsEqualTo(rows.Length);
        await Assert.That(rows.Count(row => row.Database == SiteTokens.KeyLoad)).IsEqualTo(1);
        var rates = rows.Select(row => row.Cells[SiteTokens.Load].SingleOrDefault(line => line.Nodes == 1))
            .Select(line => line?.Status == SiteTokens.Passed
                ? double.Parse(line.Value, CultureInfo.InvariantCulture) : double.NegativeInfinity).ToArray();
        await Assert.That(rates).IsEquivalentTo(rates.OrderDescending(), CollectionOrdering.Matching);
    }

    [Test]
    public async Task WriteSummaryDividesTotalCompletedOperationsByTotalMeasuredSeconds()
    {
        var page = await BuildAsync(TestContext.Current!.Execution.CancellationToken);
        var sources = SiteResultFiles.Artifacts.Select(SiteResultFiles.Read)
            .Where(result => (string)result[SiteTokens.Database]! == SiteTokens.KeyLoad && (int)result[SiteTokens.Nodes]! == 1)
            .SelectMany(result => result[SiteTokens.Cases]!.AsArray()).Select(item => item!.AsObject())
            .Where(item => new[] { "load", "update", "set", "append", "cycle" }.Contains((string)item[SiteTokens.Id]!)).ToArray();
        var operations = sources.Sum(item => (double)item[SiteTokens.Operations]!);
        var seconds = sources.Sum(item => (double)item[SiteTokens.Seconds]!);
        var row = SiteResultsPage.Row(page.Section(), "all", SiteTokens.KeyLoad);
        var line = row.Cells[Write].Single(line => line.Nodes == 1);

        await Assert.That(line.Status).IsEqualTo(SiteTokens.Passed);
        await Assert.That(Math.Abs(double.Parse(line.Value, CultureInfo.InvariantCulture) - operations / seconds)).IsLessThan(0.000001);
        await Assert.That(line.Text).Contains(sources.Length.ToString(CultureInfo.InvariantCulture) + " cases");
    }

    [Test]
    public async Task SearchSummaryKeepsFailedCasesVisible()
    {
        var page = await BuildAsync(TestContext.Current!.Execution.CancellationToken);
        var row = SiteResultsPage.Row(page.Section(), "all", SiteTokens.KeyLoad);
        var line = row.Cells[Search].Single(line => line.Nodes == 1);

        await Assert.That(line.Status).IsEqualTo(SiteTokens.Failed);
        await Assert.That(line.Value).IsEmpty();
        await Assert.That(line.Text).Contains("Traverse timed out");
    }

    [Test]
    public async Task QueueWritesAndReadsUseCompletedCyclesWithTheFullMeasuredDuration()
    {
        var page = await BuildAsync(TestContext.Current!.Execution.CancellationToken);
        var row = SiteResultsPage.Row(page.Section(), "all", "RabbitMQ");

        foreach (var nodes in new[] { 1, 3 })
        {
            var source = SiteResultFiles.Read("benchmark-rabbitmq-queue-n" + nodes.ToString(CultureInfo.InvariantCulture))
                [SiteTokens.Cases]![0]!;
            var rate = (double)source[SiteTokens.Operations]! / (double)source[SiteTokens.Seconds]!;
            foreach (var metric in new[] { Write, Read })
            {
                var line = row.Cells[metric].Single(line => line.Nodes == nodes);
                await Assert.That(line.Status).IsEqualTo(SiteTokens.Passed);
                await Assert.That(double.Parse(line.Value, CultureInfo.InvariantCulture)).IsEqualTo(rate);
                await Assert.That(line.Text).Contains("Full queue cycle");
            }
        }

        await Assert.That(row.Cells[Mixed].Length).IsEqualTo(2);
        await Assert.That(row.Cells[Mixed].All(line => line.Status == SiteTokens.Passed)).IsTrue();
    }

    [Test]
    public async Task EveryDatabaseHasWriteAndReadCasesInTheGlobalComparison()
    {
        var page = await BuildAsync(TestContext.Current!.Execution.CancellationToken);

        foreach (var row in SiteResultsPage.Rows(page.Section()))
        {
            await Assert.That(row.Cells[Write].Length).IsGreaterThan(0);
            await Assert.That(row.Cells[Read].Length).IsGreaterThan(0);
        }
    }

    [Test]
    public async Task VectorLoadingIsWritingAndVectorSearchIsReading()
    {
        var page = await BuildAsync(TestContext.Current!.Execution.CancellationToken);
        var row = SiteResultsPage.Row(page.Section(), "all", "Qdrant");
        var write = row.Cells[Write].Single(line => line.Nodes == 1);
        var read = row.Cells[Read].Single(line => line.Nodes == 1);

        await Assert.That(write.Status).IsEqualTo(SiteTokens.Passed);
        await Assert.That(read.Status).IsEqualTo(SiteTokens.Passed);
        await Assert.That(write.Value).IsEqualTo(row.Cells[SiteTokens.Load].Single(line => line.Nodes == 1).Value);
        await Assert.That(read.Value).IsEqualTo(row.Cells[Search].Single(line => line.Nodes == 1).Value);
    }

    [Test]
    public async Task TopologyChangeIsShownOnlyForTheSameCaseCoverage()
    {
        var page = await BuildAsync(TestContext.Current!.Execution.CancellationToken);
        var keyLoad = SiteResultsPage.Row(page.Section(), "all", SiteTokens.KeyLoad);
        var queue = SiteResultsPage.Row(page.Section(), "all", "RabbitMQ");

        await Assert.That(keyLoad.Cells[SiteTokens.Load].Single(line => line.Nodes == 3).Text).DoesNotContain("vs 1 node");
        await Assert.That(queue.Cells[Mixed].Single(line => line.Nodes == 3).Text).Contains("vs 1 node");
    }

    private static async Task<SiteResultsPage> BuildAsync(CancellationToken cancellationToken)
    {
        await using var workspace = SiteWorkspace.Create();
        await workspace.CopyArtifactsAsync(SiteResultFiles.Artifacts, cancellationToken);
        var merge = await workspace.MergeAsync(cancellationToken);
        await Assert.That(merge.ExitCode).IsEqualTo(SiteTokens.Success);
        var build = await workspace.BuildAsync(workspace.Merged, cancellationToken);
        await Assert.That(build.ExitCode).IsEqualTo(SiteTokens.Success);
        await Assert.That(build.StandardError).IsEmpty();
        return await SiteResultsPage.ReadAsync(workspace.Site, cancellationToken);
    }
}
