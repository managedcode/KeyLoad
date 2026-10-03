using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedProjectionTests
{
    /// <summary>AC-ISO-008: actual complete GitHub evidence survives compact projection without sample fabrication.</summary>
    [Test]
    public async Task AC_ISO_008_ProjectionRetainsEveryOriginalReportValueAndWorkerIdentity()
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        var bytes = await File.ReadAllBytesAsync(fixture.Projection, token);
        await Assert.That(bytes.Length <= 4_194_304).IsTrue();
        var projection = JsonNode.Parse(bytes)!.AsObject();
        var manifest = JsonNode.Parse(await File.ReadAllBytesAsync(
            Path.Combine(fixture.Inputs.Aggregate, "aggregate.json"), token))!.AsObject();
        var workers = projection[SiteIsolatedFields.Workers]!.AsArray();
        await Assert.That(workers.Count).IsEqualTo(270);
        foreach (var worker in workers)
        {
            var id = worker![SiteIsolatedFields.Id]!.GetValue<string>();
            var original = manifest[SiteIsolatedFields.Workers]!.AsArray().Single(item => item![SiteIsolatedFields.Id]!.GetValue<string>() == id)!;
            foreach (var property in original.AsObject())
            {
                await Assert.That(JsonNode.DeepEquals(property.Value, worker[property.Key])).IsTrue();
            }

            var raw = Path.Combine(fixture.Inputs.Aggregate, "workers", id, "worker.json");
            var envelope = JsonNode.Parse(await File.ReadAllBytesAsync(raw, token))!.AsObject();
            var report = envelope[SiteIsolatedFields.Report]?.DeepClone();
            if (report is not null)
            {
                foreach (var item in report[SiteIsolatedFields.Cases]!.AsArray())
                {
                    item!.AsObject().Remove(SiteIsolatedFields.Samples);
                }
            }

            await Assert.That(JsonNode.DeepEquals(report, worker[SiteIsolatedFields.Report])).IsTrue();
        }
    }

    /// <summary>AC-ISO-008: browser contract accepts the producer's authentic complete projection.</summary>
    [Test]
    public async Task AC_ISO_008_ActualProducerOutputPassesIndependentBrowserValidator()
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var response = await SiteIsolatedNodeProcess.RunAsync(fixture.Inputs.Site, new
        {
            operation = "validate",
            repository = fixture.Inputs.Site.Repository,
            catalog = fixture.Catalog,
            projection = fixture.Projection,
        }, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(response.GetProperty(SiteIsolatedFields.Ok).GetBoolean()).IsTrue();
        await Assert.That(response.GetProperty(SiteIsolatedFields.Result).GetInt32()).IsEqualTo(270);
    }
}
