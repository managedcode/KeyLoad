using System.Text.Json;

namespace KeyLoad.SiteTests.BenchmarkComparisons;

internal sealed class SiteIsolatedArithmeticTests
{
    private const long ObservationMaximumBytes = 24_000_000;
    private static readonly string[] Metrics = ["throughput", "p50", "p95", "p99", "errors", "enqueue", "receive", "ack", "cpu", "alloc", "rss", "duration"];

    /// <summary>AC-ISO-009: independent C# arithmetic covers all current cases, both execution topologies and original session results.</summary>
    [Test]
    public async Task AC_ISO_009_EveryMetricMatchesItsOwnWorkerWithoutCrossNodePooling()
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        using var projection = JsonDocument.Parse(await File.ReadAllBytesAsync(fixture.Projection, token));
        var selections = Selections();
        await using var temporary = SiteTempDirectory.Create();
        var output = Path.Combine(temporary.Path, "execution-observations.json");
        var response = await SiteIsolatedNodeProcess.RunAsync(fixture.Inputs.Site, new
        {
            operation = "rowsBatch",
            repository = fixture.Inputs.Site.Repository,
            projection = fixture.Projection,
            selections,
            output,
        }, token);
        await Assert.That(response.GetProperty(SiteIsolatedFields.Ok).GetBoolean()).IsTrue();
        await Assert.That(response.GetProperty(SiteIsolatedFields.Result).GetInt32()).IsEqualTo(selections.Length);
        await Assert.That(new FileInfo(output).Length <= ObservationMaximumBytes).IsTrue();
        using var observations = JsonDocument.Parse(await File.ReadAllBytesAsync(output, token));
        var values = observations.RootElement.EnumerateArray().ToArray();
        await Assert.That(values.Length).IsEqualTo(selections.Length);
        for (var index = 0; index < values.Length; index++)
        {
            var actual = values[index].GetProperty(SiteIsolatedFields.Rows).EnumerateArray().ToArray();
            var expected = SiteIsolatedOracle.Rows(projection.RootElement, selections[index]);
            await AssertRows(actual, expected);
        }
    }

    private static SiteIsolatedSelection[] Selections()
    {
        var scenarios = SiteIsolatedOracle.Cases;
        return (from scenario in scenarios
                from nodes in new[] { 1, 3 }
                from repetition in new object[] { "session" }
                from metric in Metrics
                from target in new[] { "all", "KeyLoad", "Neo4j" }
                select new SiteIsolatedSelection(scenario, nodes, repetition, metric, target)).ToArray();
    }

    internal static async Task AssertRows(JsonElement[] actual, SiteIsolatedExpectedRow[] expected)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < actual.Length; index++)
        {
            var row = actual[index];
            var oracle = expected[index];
            await Assert.That(row.GetProperty(SiteIsolatedFields.Name).GetString()).IsEqualTo(oracle.Name);
            await Assert.That(row.GetProperty(SiteIsolatedFields.Status).GetString()).IsEqualTo(oracle.Status);
            await Assert.That(row.GetProperty(SiteIsolatedFields.NodeCount).GetInt32()).IsEqualTo(oracle.NodeCount);
            await Assert.That(row.GetProperty(SiteIsolatedFields.WorkerId).GetString()).IsEqualTo(oracle.WorkerId);
            await Assert.That(row.GetProperty(SiteIsolatedFields.JobId).GetInt64()).IsEqualTo(oracle.JobId);
            await Assert.That(row.GetProperty(SiteIsolatedFields.ArtifactId).GetInt64()).IsEqualTo(oracle.ArtifactId);
            if (oracle.Value is null)
            {
                await Assert.That(row.GetProperty(SiteIsolatedFields.Value).ValueKind).IsEqualTo(JsonValueKind.Null);
            }
            else
            {
                await Assert.That(row.GetProperty(SiteIsolatedFields.Value).GetDouble()).IsEqualTo(oracle.Value.Value).Within(0.000001);
            }
        }
    }
}
