using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageClusterFixtureSourceReader
{
    private const string ClusterSuite = "cluster";
    private const string ManifestName = "functional-coverage.production-source-manifest.json";
    private const string SchemaVersionProperty = "schemaVersion";
    private const int SourceSchemaVersion = 3;
    private const int RunVersion = 1;

    internal static async Task<(string Hash, string Revision)> ReadSourceAsync(string path,
        string expectedSourceRevision, IOptions<NativeCoverageExecutionOptions> options, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Value.IsValid())
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
        }
        if (!Path.IsPathFullyQualified(path) || Path.GetFileName(path) != ManifestName)
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
        }
        var bytes = await NativeCoverageClusterBoundedFileReader.ReadAsync(path,
            Math.Min(options.Value.MaximumManifestBytes,
                NativeCoverageClusterFixtureProtocol.MaximumSourceManifestBytes),
            options, token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var revision = root.GetProperty(NativeCoverageClusterFixtureProtocol.SourceRevisionProperty).GetString();
        if (root.GetProperty(SchemaVersionProperty).GetInt32() != SourceSchemaVersion
            || string.IsNullOrWhiteSpace(revision)
            || revision != expectedSourceRevision)
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
        }
        return (Convert.ToHexStringLower(SHA256.HashData(bytes)), revision);
    }

    internal static async Task<(string RunId, IReadOnlyList<NativeCoverageClusterCaseIdentity> Cases,
        NativeCoverageClusterExecutionBounds Bounds)> ReadRunAsync(string path, string runId, string sourceHash,
        string sourceRevision, IOptions<NativeCoverageExecutionOptions> options, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(options);
        var execution = options.Value;
        if (!execution.IsValid())
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
        }
        var bytes = await NativeCoverageClusterBoundedFileReader.ReadAsync(path,
            Math.Min(execution.MaximumDescriptorBytes,
                NativeCoverageClusterFixtureProtocol.MaximumReceiptBytes),
            options, token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        ValidateRunIdentity(root, runId, sourceHash, sourceRevision);
        var policy = root.GetProperty(NativeCoverageClusterRunProtocol.ExecutionPolicyProperty);
        NativeCoverageClusterRunPolicyReader.Validate(policy);
        var cases = ReadCases(root);
        var bounds = NativeCoverageClusterRunPolicyReader.ReadBounds(root);
        if (bounds != ExpectedBounds(execution))
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidRun);
        }
        return (runId, cases, bounds);
    }

    private static void ValidateRunIdentity(JsonElement root, string runId, string sourceHash, string sourceRevision)
    {
        if (!NativeCoverageClusterFixtureArtifactValidation.HasExactProperties(root,
                NativeCoverageClusterFixtureProtocol.SchemaProperty,
                NativeCoverageClusterFixtureProtocol.RunIdProperty,
                NativeCoverageClusterRunProtocol.SuiteProperty,
                NativeCoverageClusterFixtureProtocol.SourceRevisionProperty,
                NativeCoverageClusterFixtureProtocol.SourceManifestHashProperty,
                NativeCoverageClusterRunProtocol.TestImageManifestHashProperty,
                NativeCoverageClusterRunProtocol.FilterProperty,
                NativeCoverageClusterFixtureProtocol.CasesProperty,
                NativeCoverageClusterRunProtocol.ExecutionPolicyProperty)
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.SchemaProperty).GetInt32() != RunVersion
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.RunIdProperty).GetString() != runId
            || root.GetProperty(NativeCoverageClusterRunProtocol.SuiteProperty).GetString() != ClusterSuite
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.SourceManifestHashProperty).GetString() != sourceHash
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.SourceRevisionProperty).GetString() != sourceRevision
            || root.GetProperty(NativeCoverageClusterRunProtocol.FilterProperty).GetString()
                != NativeCoverageClusterProtocol.Filter
            || !NativeCoverageClusterFixtureArtifactValidation.IsSha256(
                root.GetProperty(NativeCoverageClusterRunProtocol.TestImageManifestHashProperty).GetString() ?? string.Empty))
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidRun);
        }
    }

    private static NativeCoverageClusterCaseIdentity[] ReadCases(JsonElement root)
    {
        var cases = root.GetProperty(NativeCoverageClusterFixtureProtocol.CasesProperty).EnumerateArray()
            .Select(item => new NativeCoverageClusterCaseIdentity(
                item.GetProperty(NativeCoverageClusterFixtureProtocol.ClassProperty).GetString()!,
                item.GetProperty(NativeCoverageClusterFixtureProtocol.MethodProperty).GetString()!,
                item.GetProperty(NativeCoverageClusterFixtureProtocol.InstanceProperty).GetString()!)).ToArray();
        if (cases.Length != NativeCoverageClusterProtocol.RequiredContributorCount
            || cases.Distinct().Count() != cases.Length || !HasExactCases(cases))
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidRun);
        }
        return cases;
    }

    internal static bool IsRunIdentity(string runId, string imageReference)
        => Guid.TryParseExact(runId, NativeCoverageClusterProtocol.GuidFormat, out var parsed)
        && parsed.ToString(NativeCoverageClusterProtocol.GuidFormat) == runId
        && imageReference == NativeCoverageClusterFixtureProtocol.CoverageImagePrefix
            + parsed.ToString(NativeCoverageClusterProtocol.GuidCompactFormat);

    private static NativeCoverageClusterExecutionBounds ExpectedBounds(NativeCoverageExecutionOptions value) => new(
        value.MaximumDescriptorBytes, value.MaximumFiles, value.ReadBufferBytes, value.MaximumTotalBytes,
        value.MaximumFileBytes, value.MaximumPathCharacters, value.MaximumManifestBytes, value.MaximumReportBytes,
        value.ShutdownTimeout.ToString(NativeCoverageClusterRunProtocol.TimeSpanFormat, CultureInfo.InvariantCulture),
        value.SettlementTimeout.ToString(NativeCoverageClusterRunProtocol.TimeSpanFormat, CultureInfo.InvariantCulture),
        value.ContainerStopTimeout.ToString(NativeCoverageClusterRunProtocol.TimeSpanFormat, CultureInfo.InvariantCulture),
        value.ApplicationCleanupTimeout.ToString(NativeCoverageClusterRunProtocol.TimeSpanFormat, CultureInfo.InvariantCulture));

    private static bool HasExactCases(IReadOnlyCollection<NativeCoverageClusterCaseIdentity> cases)
    {
        var expected = NativeCoverageClusterContributorCatalog.Contributors.Select(contributor =>
            new NativeCoverageClusterCaseIdentity(contributor.ClassName, contributor.MethodName, contributor.InstanceName))
            .ToHashSet();
        return cases.All(expected.Remove) && expected.Count == 0;
    }

}
