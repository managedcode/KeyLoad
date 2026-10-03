using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedValidationTests
{
    /// <summary>AC-ISO-008: these corrupt authentic copies are parser rejection inputs, never measurement evidence.</summary>
    [Test]
    [Arguments("missing")]
    [Arguments("duplicate")]
    [Arguments("foreign")]
    [Arguments("cohort")]
    [Arguments("options")]
    [Arguments("artifact")]
    [Arguments("job")]
    [Arguments("copies")]
    [Arguments("failed")]
    [Arguments("percentiles")]
    [Arguments("throughput")]
    [Arguments("samples")]
    public async Task AC_ISO_008_BrowserRejectsIncompleteOrInconsistentProjection(string corruption)
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        var value = JsonNode.Parse(await File.ReadAllBytesAsync(fixture.Projection, token))!.AsObject();
        CorruptProjection(value, corruption);
        await using var temporary = SiteTempDirectory.Create();
        var path = Path.Combine(temporary.Path, "validation-input.json");
        await File.WriteAllTextAsync(path, value.ToJsonString(), token);
        var response = await SiteIsolatedNodeProcess.RunAsync(fixture.Inputs.Site, new
        {
            operation = "validate",
            repository = fixture.Inputs.Site.Repository,
            catalog = fixture.Catalog,
            projection = path,
        }, token);
        await Assert.That(response.GetProperty(SiteIsolatedFields.Ok).GetBoolean()).IsFalse();
    }

    /// <summary>AC-ISO-008: catalog cannot redirect measurements or conflate website and measured source.</summary>
    [Test]
    [Arguments("source")]
    [Arguments("path")]
    [Arguments("origin")]
    [Arguments("hash")]
    [Arguments("rawLocation")]
    [Arguments("extra")]
    public async Task AC_ISO_008_BrowserRejectsCorruptCatalog(string corruption)
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        var value = JsonNode.Parse(await File.ReadAllBytesAsync(fixture.Catalog, token))!.AsObject();
        switch (corruption)
        {
            case "source":
                value[SiteIsolatedFields.MeasuredSourceRevision] = new string('0', 40);
                break;
            case "path":
                value[SiteIsolatedFields.Projection]![SiteIsolatedFields.Path] = "../projection.json";
                break;
            case "origin":
                value[SiteIsolatedFields.EvidenceUrl] = "https://example.invalid/run";
                break;
            case "hash":
                value[SiteIsolatedFields.Projection]![SiteIsolatedFields.Sha256] = "invalid";
                break;
            case "rawLocation":
                value[SiteIsolatedFields.RawLocation] = "pages";
                break;
            case "extra":
                value[SiteIsolatedFields.Extra] = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption));
        }
        await using var temporary = SiteTempDirectory.Create();
        var path = Path.Combine(temporary.Path, "validation-input.json");
        await File.WriteAllTextAsync(path, value.ToJsonString(), token);
        var response = await SiteIsolatedNodeProcess.RunAsync(fixture.Inputs.Site, new
        {
            operation = "validate",
            repository = fixture.Inputs.Site.Repository,
            catalog = path,
            projection = fixture.Projection,
        }, token);
        await Assert.That(response.GetProperty(SiteIsolatedFields.Ok).GetBoolean()).IsFalse();
    }

    private static void CorruptProjection(JsonObject value, string corruption)
    {
        var workers = value[SiteIsolatedFields.Workers]!.AsArray();
        var worker = SiteIsolatedFailureFixture.TryMeasuredWorker(workers) ?? workers[0]!;
        var report = worker[SiteIsolatedFields.Report];
        if (report is null && corruption is "copies" or "failed" or "percentiles" or "throughput" or "samples")
        {
            // Retain unavailable-only rejection: no failed/unsupported worker may carry any report shape.
            worker[SiteIsolatedFields.Report] = new JsonObject();
            return;
        }
        CorruptWorker(value, workers, worker, report, corruption);
    }

    private static void CorruptWorker(JsonObject value, JsonArray workers, JsonNode worker, JsonNode? report, string corruption)
    {
        var item = report?[SiteIsolatedFields.Cases]?[0];
        switch (corruption)
        {
            case "missing":
                workers.RemoveAt(0);
                break;
            case "duplicate":
                workers[1] = workers[0]!.DeepClone();
                break;
            case "foreign":
                worker[SiteIsolatedFields.Target] = "foreign";
                break;
            case "cohort":
                (report?[SiteIsolatedFields.Provenance] ?? value[SiteIsolatedFields.Cohort])![SiteIsolatedFields.Attempt] = 999999;
                break;
            case "options":
                value[SiteIsolatedFields.Options]![SiteIsolatedFields.Operations] = 1;
                break;
            case "artifact":
                worker[SiteIsolatedFields.Artifact]![SiteIsolatedFields.Expired] = true;
                break;
            case "job":
                var job = worker[SiteIsolatedFields.Job]!;
                job[SiteIsolatedFields.Conclusion] = job[SiteIsolatedFields.Conclusion]!.GetValue<string>() == SiteIsolatedFailureFixture.Failure
                    ? SiteIsolatedFailureFixture.Success : SiteIsolatedFailureFixture.Failure;
                break;
            case "copies":
                report![SiteIsolatedFields.Targets]![0]![SiteIsolatedFields.Cluster]![SiteIsolatedFields.DataCopies] = 99;
                break;
            case "failed":
                item![SiteIsolatedFields.Status] = "failed";
                break;
            case "percentiles":
                item![SiteIsolatedFields.Measurement]![SiteIsolatedFields.Latency]![SiteIsolatedFields.P99Ms] = -1;
                break;
            case "throughput":
                item![SiteIsolatedFields.Measurement]![SiteIsolatedFields.UsefulOperationsPerSecond] = 0;
                break;
            case "samples":
                item![SiteIsolatedFields.Samples] = new JsonArray();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption));
        }
    }
}
