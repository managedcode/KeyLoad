using System.Text.Json;
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
        await Assert.That(workers.Count).IsEqualTo(SiteIsolatedInventory.ControlWorkers());
        foreach (var worker in workers)
        {
            var id = worker![SiteIsolatedFields.Id]!.GetValue<string>();
            var original = manifest[SiteIsolatedFields.Workers]!.AsArray().Single(item => item![SiteIsolatedFields.Id]!.GetValue<string>() == id)!;
            foreach (var property in original.AsObject())
            {
                await Assert.That(JsonNode.DeepEquals(property.Value, worker[property.Key])).IsTrue();
            }

            var raw = Path.Combine(fixture.Inputs.Aggregate, SiteIsolatedFields.Workers, id, "session.json");
            var envelope = JsonNode.Parse(await File.ReadAllBytesAsync(raw, token))!.AsObject();
            await Assert.That(JsonNode.DeepEquals(envelope, worker[SiteIsolatedFields.Session])).IsTrue();
            foreach (var reference in envelope[SiteIsolatedFields.Cases]!.AsArray())
            {
                await using var stream = File.OpenRead(Path.Combine(fixture.Inputs.Aggregate,
                    SiteIsolatedFields.Workers, id, reference![SiteIsolatedFields.Path]!.GetValue<string>()));
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                var report = Compact(document.RootElement);

                var compact = worker[SiteIsolatedFields.Reports]!.AsArray().Single(item => item![SiteIsolatedFields.CaseId]!.GetValue<string>() == report[SiteIsolatedFields.CaseId]!.GetValue<string>());
                await Assert.That(JsonNode.DeepEquals(report, compact)).IsTrue();
            }
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
        await Assert.That(response.GetProperty(SiteIsolatedFields.Result).GetInt32()).IsEqualTo(SiteIsolatedInventory.ControlWorkers());
    }
    private static JsonObject Compact(JsonElement report)
    {
        var compact = new JsonObject();
        foreach (var property in report.EnumerateObject())
        {
            compact[property.Name] = property.Name == SiteIsolatedFields.Lanes
                ? CompactLanes(property.Value) : JsonNode.Parse(property.Value.GetRawText());
        }

        return compact;
    }

    private static JsonArray CompactLanes(JsonElement lanes)
    {
        var result = new JsonArray();
        foreach (var lane in lanes.EnumerateArray())
        {
            var value = new JsonObject();
            foreach (var field in lane.EnumerateObject().Where(field => field.Name != SiteIsolatedFields.Samples))
            {
                value[field.Name] = JsonNode.Parse(field.Value.GetRawText());
            }

            result.Add(value);
        }

        return result;
    }
}
