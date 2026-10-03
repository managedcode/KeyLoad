using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>Controlled no-data parser copies retain genuine measured values unchanged and never qualify publication.</summary>
internal static class SiteIsolatedFailureFixture
{
    internal const string Failed = "failed";
    internal const string Failure = "failure";
    internal const string Success = "success";
    internal const string Reason = "Benchmark failed; no measurement data is available.";
    internal const string WorkloadStep = "Run database workload";
    internal const string UploadStep = "Save benchmark results";
    internal const string ProjectionRelativePath = "data/isolated/projection.json";
    internal const string CatalogRelativePath = "data/isolated-catalog.json";

    public static JsonNode? TryMeasuredWorker(JsonArray workers)
        => workers.FirstOrDefault(worker => worker?[SiteIsolatedFields.Report]?[SiteIsolatedFields.Cases] is JsonArray cases &&
            cases.Any(item => item?[SiteIsolatedFields.Measurement] is not null));

    public static JsonNode FailedWorker(JsonObject projection, bool all)
    {
        var workers = projection[SiteIsolatedFields.Workers]!.AsArray();
        var first = workers.First(worker => worker is not null)!;
        if (all)
        {
            foreach (var worker in workers)
            {
                SetFailed(worker!);
            }
        }
        else
        {
            SetFailed(first);
        }
        if (workers.All(worker => worker?[SiteIsolatedFields.Report] is null))
        {
            projection[SiteIsolatedFields.DatasetSha256] = null;
        }
        return first;
    }

    private static void SetFailed(JsonNode worker)
    {
        worker[SiteIsolatedFields.Disposition] = Failed;
        worker[SiteIsolatedFields.Reason] = Reason;
        worker[SiteIsolatedFields.Report] = null;
        var job = worker[SiteIsolatedFields.Job]!;
        job[SiteIsolatedFields.Conclusion] = Failure;
        foreach (var step in job[SiteIsolatedFields.Steps]!.AsArray())
        {
            step![SiteIsolatedFields.Conclusion] = step[SiteIsolatedFields.Name]!.GetValue<string>() == WorkloadStep ? Failure : Success;
        }
    }

    public static async Task<JsonObject> WriteBrowserCopyAsync(SiteBrowserSession browser, bool all, CancellationToken token)
    {
        var projectionPath = Path.Combine(browser.Output, ProjectionRelativePath);
        var projection = JsonNode.Parse(await File.ReadAllBytesAsync(projectionPath, token))!.AsObject();
        FailedWorker(projection, all);
        var bytes = System.Text.Encoding.UTF8.GetBytes(projection.ToJsonString());
        await File.WriteAllBytesAsync(projectionPath, bytes, token);
        var catalogPath = Path.Combine(browser.Output, CatalogRelativePath);
        var catalog = JsonNode.Parse(await File.ReadAllBytesAsync(catalogPath, token))!.AsObject();
        catalog[SiteIsolatedFields.Projection]![SiteIsolatedFields.Sha256] = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await File.WriteAllTextAsync(catalogPath, catalog.ToJsonString(), token);
        return projection;
    }
}
