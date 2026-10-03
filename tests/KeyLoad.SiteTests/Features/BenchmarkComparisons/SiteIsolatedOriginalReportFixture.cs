using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>Immutable original provider data is a test input only; it never substitutes for current publication evidence.</summary>
internal static class SiteIsolatedOriginalReportFixture
{
    internal const string RelativePath = "tests/KeyLoad.SiteTests/Features/BenchmarkComparisons/OriginalPostgresReport.json";
    internal const string ExpectedSha256 = "63753f3633b50037b34b7449c4fa9a4a4b2cad56fa0bcec45a5531bc39c7c33b";
    internal const string QueueRelativePath = "tests/KeyLoad.SiteTests/Features/BenchmarkComparisons/OriginalRabbitQueueReport.json";
    internal const string QueueExpectedSha256 = "4927c2bee4fed800a163e93fc9308fedeff943b7454f0a66549a545518b1956f";
    internal const string OriginalSource = "7136d6e1d9ae3bb91a092d35a62ddcef7131843c";
    internal const string PurposeField = "purpose";
    internal const string OriginalField = "original";
    internal const string JobUrlField = "jobUrl";
    internal const string ArtifactIdField = "artifactId";
    internal const string RowField = "row";
    internal const string TestPurpose = "Immutable original GitHub measurement for parser and arithmetic tests only; never publication input.";
    internal const long OriginalRun = 37154664616;
    internal const long OriginalArtifact = 11285823608;
    internal const long OriginalJob = 111299653748;
    internal const long QueueOriginalArtifact = 11285807871;
    internal const long QueueOriginalJob = 111299652838;
    internal const string OriginalJobUrl = "https://github.com/managedcode/KeyLoad/actions/runs/37154664616/job/111299653748";
    internal const string QueueOriginalJobUrl = "https://github.com/managedcode/KeyLoad/actions/runs/37154664616/job/111299652838";
    internal const string Operation = "originalReport";
    internal const string MedianSelection = "median";
    internal const string DefaultMetric = "throughput";
    internal const int OriginalBytes = 7958;
    internal const int QueueOriginalBytes = 9702;

    public static async Task<byte[]> ReadAsync(SiteTestInputs inputs, CancellationToken token, bool queue = false)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(inputs.Repository, queue ? QueueRelativePath : RelativePath), token);
        await Assert.That(bytes.Length).IsEqualTo(queue ? QueueOriginalBytes : OriginalBytes);
        await Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes))).IsEqualTo(queue ? QueueExpectedSha256 : ExpectedSha256);
        using var fixture = JsonDocument.Parse(bytes);
        var value = fixture.RootElement;
        await Assert.That(value.GetProperty(PurposeField).GetString()).IsEqualTo(TestPurpose);
        await Assert.That(value.GetProperty(SiteIsolatedFields.Cohort).GetProperty(SiteIsolatedFields.RunId).GetInt64()).IsEqualTo(OriginalRun);
        await Assert.That(value.GetProperty(SiteIsolatedFields.Cohort).GetProperty(SiteIsolatedFields.SourceRevision).GetString()).IsEqualTo(OriginalSource);
        await Assert.That(value.GetProperty(OriginalField).GetProperty(ArtifactIdField).GetInt64()).IsEqualTo(queue ? QueueOriginalArtifact : OriginalArtifact);
        await Assert.That(value.GetProperty(OriginalField).GetProperty(JobUrlField).GetString()).IsEqualTo(queue ? QueueOriginalJobUrl : OriginalJobUrl);
        return bytes;
    }

    public static Task<JsonElement> ProbeAsync(SiteTestInputs inputs, string? corruption, object repetition, string metric,
        CancellationToken token, bool queue = false)
        => SiteIsolatedNodeProcess.RunAsync(inputs, new
        {
            operation = Operation,
            repository = inputs.Repository,
            fixture = Path.Combine(inputs.Repository, queue ? QueueRelativePath : RelativePath),
            corruption,
            repetition,
            metric,
        }, token);

    public static JsonDocument OracleProjection(JsonElement fixture, bool queue = false)
    {
        var worker = JsonNode.Parse(fixture.GetProperty(SiteIsolatedFields.Worker).GetRawText())!.AsObject();
        worker[SiteIsolatedFields.Job] = new JsonObject { [SiteIsolatedFields.Id] = queue ? QueueOriginalJob : OriginalJob };
        worker[SiteIsolatedFields.Artifact] = new JsonObject { [SiteIsolatedFields.Id] = queue ? QueueOriginalArtifact : OriginalArtifact };
        worker[SiteIsolatedFields.Report] = JsonNode.Parse(fixture.GetProperty(SiteIsolatedFields.Report).GetRawText());
        var projection = new JsonObject { [SiteIsolatedFields.Workers] = new JsonArray(worker) };
        return JsonDocument.Parse(projection.ToJsonString());
    }
}
