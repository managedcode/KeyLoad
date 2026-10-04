using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedProducerFixture
{
    // This is an explicitly invalid parser directory. Symlinks are native rejection inputs, never valid evidence.
    public static async Task<bool> CreateAsync(SiteIsolatedFixture fixture, string input, string corruption, CancellationToken token)
    {
        Directory.CreateDirectory(Path.Combine(input, "workers"));
        var manifest = JsonNode.Parse(await File.ReadAllBytesAsync(
            Path.Combine(fixture.Inputs.Aggregate, "aggregate.json"), token))!.AsObject();
        var workers = manifest[SiteIsolatedFields.Workers]!.AsArray();
        var projection = JsonNode.Parse(await File.ReadAllBytesAsync(fixture.Projection, token))!.AsObject();
        var measured = SiteIsolatedFailureFixture.TryMeasuredWorker(projection[SiteIsolatedFields.Workers]!.AsArray());
        var selectedId = measured?[SiteIsolatedFields.Id]!.GetValue<string>() ?? workers[0]![SiteIsolatedFields.Id]!.GetValue<string>();
        var worker = workers.Single(item => item![SiteIsolatedFields.Id]!.GetValue<string>() == selectedId)!;
        workers.Remove(worker);
        workers.Insert(0, worker);
        foreach (var entry in workers)
        {
            var id = entry![SiteIsolatedFields.Id]!.GetValue<string>();
            var directory = Path.Combine(input, "workers", id);
            Directory.CreateDirectory(directory);
            File.CreateSymbolicLink(Path.Combine(directory, "worker.json"),
                Path.Combine(fixture.Inputs.Aggregate, "workers", id, "worker.json"));
        }
        await CorruptAsync(fixture, input, manifest, corruption, token);
        await File.WriteAllTextAsync(Path.Combine(input, "aggregate.json"), manifest.ToJsonString(), token);
        return measured is not null;
    }

    private static async Task CorruptAsync(SiteIsolatedFixture fixture, string input, JsonObject manifest,
        string corruption, CancellationToken token)
    {
        var workers = manifest[SiteIsolatedFields.Workers]!.AsArray();
        var first = workers[0]!;
        switch (corruption)
        {
            case "missing":
                Directory.Delete(Path.Combine(input, "workers", first[SiteIsolatedFields.Id]!.GetValue<string>()), true);
                break;
            case "foreign":
                Directory.CreateDirectory(Path.Combine(input, "workers", "foreign"));
                break;
            case "duplicate":
                workers[1] = first.DeepClone();
                break;
            case "expired":
                first[SiteIsolatedFields.Artifact]![SiteIsolatedFields.Expired] = true;
                break;
            case "failedJob":
                var job = first[SiteIsolatedFields.Job]!;
                job[SiteIsolatedFields.Conclusion] = job[SiteIsolatedFields.Conclusion]!.GetValue<string>() == SiteIsolatedFailureFixture.Failure
                    ? SiteIsolatedFailureFixture.Success : SiteIsolatedFailureFixture.Failure;
                break;
            case "options":
                manifest[SiteIsolatedFields.Options]![SiteIsolatedFields.Operations] = 1;
                break;
            case "unsafeRawPath":
                first[SiteIsolatedFields.RawPath] = "../worker.json";
                break;
            case "rawSymlink":
                break;
            case "rawHash":
            case "failed":
            case "unixHost":
            case "mixedDataset":
                await CorruptRawAsync(fixture, input, first, corruption, token);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption));
        }
    }

    private static async Task CorruptRawAsync(SiteIsolatedFixture fixture, string input, JsonNode worker,
        string corruption, CancellationToken token)
    {
        var id = worker[SiteIsolatedFields.Id]!.GetValue<string>();
        var source = Path.Combine(fixture.Inputs.Aggregate, "workers", id, "worker.json");
        var destination = Path.Combine(input, "workers", id, "worker.json");
        File.Delete(destination);
        if (corruption == "rawHash")
        {
            File.Copy(source, destination);
            await File.AppendAllTextAsync(destination, " ", token);
            return;
        }
        var envelope = JsonNode.Parse(await File.ReadAllBytesAsync(source, token))!.AsObject();
        if (envelope[SiteIsolatedFields.Report] is null)
        {
            // An unavailable envelope must reject every non-null report, even a deliberately empty parser object.
            envelope[SiteIsolatedFields.Report] = new JsonObject();
        }
        else
        {
            CorruptMeasuredReport(envelope, corruption);
        }
        var bytes = System.Text.Encoding.UTF8.GetBytes(envelope.ToJsonString());
        worker[SiteIsolatedFields.RawSha256] = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await File.WriteAllBytesAsync(destination, bytes, token);
    }

    private static void CorruptMeasuredReport(JsonObject envelope, string corruption)
    {
        switch (corruption)
        {
            case "failed":
                envelope[SiteIsolatedFields.Report]![SiteIsolatedFields.Cases]![0]![SiteIsolatedFields.Status] = "failed";
                break;
            case "unixHost":
                envelope[SiteIsolatedFields.Report]![SiteIsolatedFields.HostOs] = "Windows";
                break;
            case "mixedDataset":
                envelope[SiteIsolatedFields.Report]![SiteIsolatedFields.DatasetSha256] = new string('0', 64);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption));
        }
    }
}
