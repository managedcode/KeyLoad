using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedFailedCellRejectionTests
{
    /// <summary>AC-BC-FAIL-003: unavailable results cannot carry metrics, fake success, failed uploads or arbitrary reasons.</summary>
    [Test]
    [Arguments("successJob")]
    [Arguments("successWorkload")]
    [Arguments("failedUpload")]
    [Arguments("reason")]
    [Arguments("report")]
    [Arguments("allFailedDigest")]
    [Arguments("missing")]
    public async Task AC_BC_FAIL_003_FailureRequiresExactProofAndNullReport(string corruption)
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        var projection = JsonNode.Parse(await File.ReadAllBytesAsync(fixture.Projection, token))!.AsObject();
        var worker = SiteIsolatedFailureFixture.FailedWorker(projection, corruption == "allFailedDigest");
        Corrupt(projection, worker, corruption);
        await using var temporary = SiteTempDirectory.Create();
        var path = Path.Combine(temporary.Path, "invalid-no-data-parser-copy.json");
        await File.WriteAllTextAsync(path, projection.ToJsonString(), token);
        var response = await SiteIsolatedFailedCellTests.ValidateAsync(fixture, path, token);
        await Assert.That(response.GetProperty(SiteIsolatedFields.Ok).GetBoolean()).IsFalse();
        await Assert.That(response.GetProperty(SiteIsolatedFields.Error).GetString()).IsEqualTo("E_ISOLATED_EVIDENCE");
    }

    private static void Corrupt(JsonObject projection, JsonNode worker, string corruption)
    {
        switch (corruption)
        {
            case "successJob":
                worker[SiteIsolatedFields.Job]![SiteIsolatedFields.Conclusion] = SiteIsolatedFailureFixture.Success;
                break;
            case "successWorkload":
            case "failedUpload":
                var name = corruption == "successWorkload" ? SiteIsolatedFailureFixture.WorkloadStep : SiteIsolatedFailureFixture.UploadStep;
                var step = worker[SiteIsolatedFields.Job]![SiteIsolatedFields.Steps]!.AsArray().Single(item => item![SiteIsolatedFields.Name]!.GetValue<string>() == name)!;
                step[SiteIsolatedFields.Conclusion] = corruption == "successWorkload" ? SiteIsolatedFailureFixture.Success : SiteIsolatedFailureFixture.Failure;
                break;
            case "reason":
                worker[SiteIsolatedFields.Reason] = "untrusted diagnostic text";
                break;
            case "report":
                worker[SiteIsolatedFields.Report] = new JsonObject();
                break;
            case "allFailedDigest":
                projection[SiteIsolatedFields.DatasetSha256] = new string('0', 64);
                break;
            case "missing":
                projection[SiteIsolatedFields.Workers]!.AsArray().RemoveAt(0);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption));
        }
    }
}
