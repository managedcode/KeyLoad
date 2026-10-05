using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteIsolatedFixture(SiteIsolatedInputs Inputs, string Root, string Catalog, string Projection)
{
    private static readonly Lazy<Task<SiteIsolatedFixture>> Current = new(CreateAsync);
    public static Task<SiteIsolatedFixture> ReadAsync() => Current.Value;

    private static async Task<SiteIsolatedFixture> CreateAsync()
    {
        var inputs = SiteIsolatedInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        var coverage = Environment.GetEnvironmentVariable(SiteBrowserTokens.CoverageEnvironment);
        if (string.IsNullOrWhiteSpace(coverage) || !Path.IsPathFullyQualified(coverage))
        {
            throw new InvalidOperationException(SiteIsolatedInputs.MissingEvidence);
        }

        var root = Path.Combine(coverage, "isolated-site-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "isolated"));
        var projectionPath = Path.Combine(root, "isolated", "projection.json");
        var result = await SiteIsolatedNodeProcess.RunAsync(inputs.Site, new
        {
            operation = "produce",
            repository = inputs.Site.Repository,
            input = inputs.Aggregate,
            output = projectionPath,
        }, token);
        if (!result.GetProperty(SiteIsolatedFields.Ok).GetBoolean() || result.GetProperty(SiteIsolatedFields.Result).GetProperty(SiteIsolatedFields.Workers).GetInt32() != SiteIsolatedInventory.ControlWorkers(inputs.Site.MeasuredRevision))
        {
            throw new InvalidOperationException(SiteIsolatedInputs.MissingEvidence);
        }

        var aggregate = await File.ReadAllBytesAsync(Path.Combine(inputs.Aggregate, "aggregate.json"), token);
        await File.WriteAllBytesAsync(Path.Combine(root, "isolated", "aggregate.json"), aggregate, token);
        var manifest = JsonNode.Parse(aggregate)!.AsObject();
        var catalog = CreateCatalog(inputs, manifest, aggregate, await File.ReadAllBytesAsync(projectionPath, token));
        var catalogPath = Path.Combine(root, "catalog.json");
        await File.WriteAllBytesAsync(catalogPath, JsonSerializer.SerializeToUtf8Bytes(catalog), token);
        return new(inputs, root, catalogPath, projectionPath);
    }

    private static object CreateCatalog(SiteIsolatedInputs inputs, JsonObject manifest, byte[] aggregate, byte[] projection)
        => new
        {
            schemaVersion = 1,
            generatedAt = TimeProvider.System.GetUtcNow().ToString("O"),
            siteSourceRevision = inputs.Site.SiteRevision,
            measuredSourceRevision = manifest[SiteIsolatedFields.Cohort]![SiteIsolatedFields.SourceRevision]!.GetValue<string>(),
            evidenceUrl = SiteTokens.EvidenceBase + manifest[SiteIsolatedFields.Cohort]![SiteIsolatedFields.RunId]!.GetValue<long>(),
            cohort = manifest[SiteIsolatedFields.Cohort],
            aggregate = new { path = "isolated/aggregate.json", sha256 = Hash(aggregate) },
            projection = new { path = "isolated/projection.json", sha256 = Hash(projection) },
            rawLocation = "githubActionsArtifacts",
        };

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
