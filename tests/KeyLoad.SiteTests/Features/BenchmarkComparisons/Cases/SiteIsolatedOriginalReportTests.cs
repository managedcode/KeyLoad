using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedOriginalReportTests
{
    private static readonly string[] Metrics = ["throughput", "p50", "p95", "p99", "errors", "enqueue", "receive", "ack", "cpu", "alloc", "rss"];

    /// <summary>AC-BC-FAIL-004: native arithmetic remains exercised by immutable original measurements even when current workers fail.</summary>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AC_BC_FAIL_004_OriginalProviderReportKeepsItsOwnSourceAndArithmetic(bool queue)
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        using var fixture = JsonDocument.Parse(await SiteIsolatedOriginalReportFixture.ReadAsync(inputs, token, queue));
        var worker = fixture.RootElement.GetProperty(SiteIsolatedFields.Worker);
        using var projection = SiteIsolatedOriginalReportFixture.OracleProjection(fixture.RootElement, queue);
        foreach (var repetition in new object[] { "all", 0, 1, 2, 3, 4 })
        {
            foreach (var metric in Metrics)
            {
                var response = await SiteIsolatedOriginalReportFixture.ProbeAsync(inputs, null,
                    repetition is string ? SiteIsolatedOriginalReportFixture.MedianSelection : repetition, metric, token, queue);
                await Assert.That(response.GetProperty(SiteIsolatedFields.Ok).GetBoolean()).IsTrue();
                var result = response.GetProperty(SiteIsolatedFields.Result);
                await Assert.That(result.GetProperty(SiteIsolatedFields.SourceRevision).GetString()).IsEqualTo(SiteIsolatedOriginalReportFixture.OriginalSource);
                await Assert.That(result.GetProperty(SiteIsolatedFields.Provenance).GetProperty(SiteIsolatedFields.RunId).GetInt64()).IsEqualTo(SiteIsolatedOriginalReportFixture.OriginalRun);
                var selection = new SiteIsolatedSelection(worker.GetProperty(SiteIsolatedFields.Scenario).GetString()!,
                    worker.GetProperty(SiteIsolatedFields.NodeCount).GetInt32(), repetition, metric, worker.GetProperty(SiteIsolatedFields.Target).GetString()!);
                var expected = SiteIsolatedOracle.Rows(projection.RootElement, selection).Single();
                var actual = result.GetProperty(SiteIsolatedOriginalReportFixture.RowField).GetProperty(SiteIsolatedFields.Value);
                if (expected.Value is null)
                {
                    await Assert.That(actual.ValueKind).IsEqualTo(JsonValueKind.Null);
                }
                else
                {
                    await Assert.That(actual.GetDouble()).IsEqualTo(expected.Value.Value).Within(0.000001);
                }
            }
        }
        await SiteIsolatedOriginalReportFixture.ReadAsync(inputs, token, queue);
    }
}
