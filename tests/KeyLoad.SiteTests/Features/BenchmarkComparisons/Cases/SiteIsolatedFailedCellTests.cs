using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedFailedCellTests
{
    private static readonly string[] Metrics = ["throughput", "p50", "p95", "p99", "errors", "enqueue", "receive", "ack", "cpu", "alloc", "rss"];
    private static readonly string[] UnavailableFields = ["value", "min", "max", "attempts", "successes", "failures", "throughput", "p50", "p95", "p99"];

    /// <summary>AC-BC-FAIL-003/004: a controlled failed-cell copy preserves competitors and contains no invented numbers.</summary>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AC_BC_FAIL_004_FailedCellsRetainIdentityAndHaveNoMetrics(bool all)
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        var projection = JsonNode.Parse(await File.ReadAllBytesAsync(fixture.Projection, token))!.AsObject();
        var originals = projection[SiteIsolatedFields.Workers]!.AsArray().Select(worker => worker!.DeepClone()).ToArray();
        var failed = SiteIsolatedFailureFixture.FailedWorker(projection, all);
        await using var temporary = SiteTempDirectory.Create();
        var path = Path.Combine(temporary.Path, "no-data-parser-copy.json");
        await File.WriteAllTextAsync(path, projection.ToJsonString(), token);
        var validation = await ValidateAsync(fixture, path, token);
        await Assert.That(validation.GetProperty(SiteIsolatedFields.Ok).GetBoolean()).IsTrue();
        await Assert.That(validation.GetProperty(SiteIsolatedFields.Result).GetInt32()).IsEqualTo(270);
        if (!all)
        {
            var workers = projection[SiteIsolatedFields.Workers]!.AsArray();
            foreach (var original in originals.Where(worker => worker[SiteIsolatedFields.Id]!.GetValue<string>() != failed[SiteIsolatedFields.Id]!.GetValue<string>()))
            {
                await Assert.That(JsonNode.DeepEquals(original, workers.Single(worker => worker![SiteIsolatedFields.Id]!.GetValue<string>() == original[SiteIsolatedFields.Id]!.GetValue<string>()))).IsTrue();
            }
        }
        await AssertUnavailableRowsAsync(fixture, path, failed, token);
    }

    private static async Task AssertUnavailableRowsAsync(SiteIsolatedFixture fixture, string path, JsonNode failed, CancellationToken token)
    {
        foreach (var metric in Metrics)
        {
            var response = await SiteIsolatedNodeProcess.RunAsync(fixture.Inputs.Site, new
            {
                operation = "rows",
                repository = fixture.Inputs.Site.Repository,
                projection = path,
                scenario = failed[SiteIsolatedFields.Scenario]!.GetValue<string>(),
                nodeCount = failed[SiteIsolatedFields.NodeCount]!.GetValue<int>(),
                repetition = "all",
                metric,
                target = failed[SiteIsolatedFields.Target]!.GetValue<string>(),
            }, token);
            await Assert.That(response.GetProperty(SiteIsolatedFields.Ok).GetBoolean()).IsTrue();
            var rows = response.GetProperty(SiteIsolatedFields.Result).EnumerateArray().ToArray();
            await Assert.That(rows.Length).IsEqualTo(1);
            var row = rows[0];
            await Assert.That(row.GetProperty(SiteIsolatedFields.Status).GetString()).IsEqualTo(SiteIsolatedFailureFixture.Failed);
            foreach (var field in UnavailableFields)
            {
                await Assert.That(row.GetProperty(field).ValueKind).IsEqualTo(JsonValueKind.Null);
            }
            await Assert.That(row.GetProperty(SiteIsolatedFields.Detail).GetString()).IsEqualTo(SiteIsolatedFailureFixture.Reason);
            await Assert.That(row.GetProperty(SiteIsolatedFields.Worker).GetProperty(SiteIsolatedFields.Job).GetProperty(SiteIsolatedFields.Id).GetInt64()).IsEqualTo(failed[SiteIsolatedFields.Job]![SiteIsolatedFields.Id]!.GetValue<long>());
        }
    }

    internal static Task<JsonElement> ValidateAsync(SiteIsolatedFixture fixture, string path, CancellationToken token)
        => SiteIsolatedNodeProcess.RunAsync(fixture.Inputs.Site, new
        {
            operation = "validate",
            repository = fixture.Inputs.Site.Repository,
            catalog = fixture.Catalog,
            projection = path,
        }, token);
}
