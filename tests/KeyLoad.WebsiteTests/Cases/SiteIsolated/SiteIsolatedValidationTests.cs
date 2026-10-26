using System.Text.Json.Nodes;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed class SiteIsolatedValidationTests
{
    /// <summary>AC-ISO-008: these corrupt authentic copies are parser rejection inputs, never measurement evidence.</summary>
    [Test]
    [Arguments("empty")]
    [Arguments("duplicate")]
    [Arguments("foreign")]
    [Arguments(SiteIsolatedFields.Cohort)]
    [Arguments("options")]
    [Arguments(SiteIsolatedFields.Artifact)]
    [Arguments(SiteIsolatedFields.Job)]
    [Arguments("copies")]
    [Arguments("failed")]
    public async Task AC_ISO_008_BrowserRejectsEmptyOrInconsistentProjection(string corruption)
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

    [Test]
    public async Task AC_ISO_008_BrowserAcceptsPartialProjectionAndKeepsAbsentRowsEmpty()
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        var original = await File.ReadAllBytesAsync(fixture.Projection, token);
        var value = JsonNode.Parse(original)!.AsObject();
        var workers = value[SiteIsolatedFields.Workers]!.AsArray();
        while (workers.Count > 1)
        {
            workers.RemoveAt(workers.Count - 1);
        }

        await using var temporary = SiteTempDirectory.Create();
        var projection = Path.Combine(temporary.Path, "partial-projection.json");
        var output = Path.Combine(temporary.Path, "partial-rows.json");
        await File.WriteAllTextAsync(projection, value.ToJsonString(), token);
        var accepted = await SiteIsolatedNodeProcess.RunAsync(fixture.Inputs.Site, new
        {
            operation = "validate",
            repository = fixture.Inputs.Site.Repository,
            catalog = fixture.Catalog,
            projection,
        }, token);
        await Assert.That(accepted.GetProperty(SiteIsolatedFields.Ok).GetBoolean()).IsTrue();
        await Assert.That(accepted.GetProperty(SiteIsolatedFields.Result).GetInt32()).IsEqualTo(1);
        var selection = new SiteIsolatedSelection("sequential-read",
            workers[0]![SiteIsolatedFields.NodeCount]!.GetValue<int>(), "session", "throughput", "all");
        var rows = await SiteIsolatedNodeProcess.RunAsync(fixture.Inputs.Site, new
        {
            operation = "rowsBatch",
            repository = fixture.Inputs.Site.Repository,
            projection,
            selections = new[] { selection },
            output,
        }, token);
        await Assert.That(rows.GetProperty(SiteIsolatedFields.Ok).GetBoolean()).IsTrue();
        using var observed = System.Text.Json.JsonDocument.Parse(await File.ReadAllBytesAsync(output, token));
        using var expected = System.Text.Json.JsonDocument.Parse(value.ToJsonString());
        await SiteIsolatedArithmeticTests.AssertRows(observed.RootElement[0].GetProperty(SiteIsolatedFields.Rows).EnumerateArray().ToArray(),
            SiteIsolatedOracle.Rows(expected.RootElement, selection));
        await Assert.That((await File.ReadAllBytesAsync(fixture.Projection, token)).SequenceEqual(original)).IsTrue();
    }

    /// <summary>AC-ISO-008: catalog cannot redirect measurements or conflate website and measured source.</summary>
    [Test]
    [Arguments(SiteIsolatedFields.Source)]
    [Arguments(SiteIsolatedFields.Path)]
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
            case SiteIsolatedFields.Source:
                value[SiteIsolatedFields.MeasuredSourceRevision] = new string('0', 40);
                break;
            case SiteIsolatedFields.Path:
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
        var worker = workers[0]!;
        CorruptWorker(value, workers, worker, corruption);
    }

    private static void CorruptWorker(JsonObject value, JsonArray workers, JsonNode worker, string corruption)
    {
        switch (corruption)
        {
            case "empty":
                workers.Clear();
                break;
            case "duplicate":
                workers.Add(workers[0]!.DeepClone());
                break;
            case "foreign":
                worker[SiteIsolatedFields.Target] = "foreign";
                break;
            case SiteIsolatedFields.Cohort:
                worker[SiteIsolatedFields.Session]![SiteIsolatedFields.Attempt] = 999999;
                break;
            case "options":
                value[SiteIsolatedFields.Options]![SiteIsolatedFields.Operations] = 1;
                break;
            case SiteIsolatedFields.Artifact:
                worker[SiteIsolatedFields.Artifact]![SiteIsolatedFields.Expired] = true;
                break;
            case SiteIsolatedFields.Job:
                worker[SiteIsolatedFields.Job]![SiteIsolatedFields.Conclusion] = "cancelled";
                break;
            case "copies":
                worker[SiteIsolatedFields.Session]![SiteIsolatedFields.TargetProfile] = new JsonObject();
                break;
            case "failed":
                worker[SiteIsolatedFields.Session]![SiteIsolatedFields.Outcome] = "invalid";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption));
        }
    }
}
