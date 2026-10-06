using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageRf3FixtureSourceReader
{
    private const string Rf3Suite = "rf3";
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
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        }
        if (!Path.IsPathFullyQualified(path) || Path.GetFileName(path) != ManifestName)
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        }
        var bytes = await NativeCoverageRf3BoundedFileReader.ReadAsync(path,
            Math.Min(options.Value.MaximumManifestBytes,
                NativeCoverageRf3FixtureProtocol.MaximumSourceManifestBytes),
            options, token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var revision = root.GetProperty(NativeCoverageRf3FixtureProtocol.SourceRevisionProperty).GetString();
        if (root.GetProperty(SchemaVersionProperty).GetInt32() != SourceSchemaVersion
            || string.IsNullOrWhiteSpace(revision)
            || revision != expectedSourceRevision)
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        }
        return (Convert.ToHexStringLower(SHA256.HashData(bytes)), revision);
    }

    internal static async Task<(string RunId, IReadOnlyList<NativeCoverageRf3CaseIdentity> Cases,
        NativeCoverageRf3ExecutionBounds Bounds)> ReadRunAsync(string path, string runId, string sourceHash,
        string sourceRevision, IOptions<NativeCoverageExecutionOptions> options, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(options);
        var execution = options.Value;
        if (!execution.IsValid())
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        }
        var bytes = await NativeCoverageRf3BoundedFileReader.ReadAsync(path,
            Math.Min(execution.MaximumDescriptorBytes,
                NativeCoverageRf3FixtureProtocol.MaximumReceiptBytes),
            options, token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        ValidateRunIdentity(root, runId, sourceHash, sourceRevision);
        var policy = root.GetProperty(NativeCoverageRf3RunProtocol.ExecutionPolicyProperty);
        NativeCoverageRf3RunPolicyReader.Validate(policy);
        var cases = ReadCases(root);
        var bounds = NativeCoverageRf3RunPolicyReader.ReadBounds(root);
        if (bounds != ExpectedBounds(execution))
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidRun);
        }
        return (runId, cases, bounds);
    }

    private static void ValidateRunIdentity(JsonElement root, string runId, string sourceHash, string sourceRevision)
    {
        if (!NativeCoverageRf3FixtureArtifactValidation.HasExactProperties(root,
                NativeCoverageRf3FixtureProtocol.SchemaProperty,
                NativeCoverageRf3FixtureProtocol.RunIdProperty,
                NativeCoverageRf3RunProtocol.SuiteProperty,
                NativeCoverageRf3FixtureProtocol.SourceRevisionProperty,
                NativeCoverageRf3FixtureProtocol.SourceManifestHashProperty,
                NativeCoverageRf3RunProtocol.TestImageManifestHashProperty,
                NativeCoverageRf3RunProtocol.FilterProperty,
                NativeCoverageRf3FixtureProtocol.CasesProperty,
                NativeCoverageRf3RunProtocol.ExecutionPolicyProperty)
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.SchemaProperty).GetInt32() != RunVersion
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.RunIdProperty).GetString() != runId
            || root.GetProperty(NativeCoverageRf3RunProtocol.SuiteProperty).GetString() != Rf3Suite
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.SourceManifestHashProperty).GetString() != sourceHash
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.SourceRevisionProperty).GetString() != sourceRevision
            || root.GetProperty(NativeCoverageRf3RunProtocol.FilterProperty).GetString()
                != NativeCoverageRf3Protocol.Filter
            || !NativeCoverageRf3FixtureArtifactValidation.IsSha256(
                root.GetProperty(NativeCoverageRf3RunProtocol.TestImageManifestHashProperty).GetString() ?? string.Empty))
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidRun);
        }
    }

    private static NativeCoverageRf3CaseIdentity[] ReadCases(JsonElement root)
    {
        var cases = root.GetProperty(NativeCoverageRf3FixtureProtocol.CasesProperty).EnumerateArray()
            .Select(item => new NativeCoverageRf3CaseIdentity(
                item.GetProperty(NativeCoverageRf3FixtureProtocol.ClassProperty).GetString()!,
                item.GetProperty(NativeCoverageRf3FixtureProtocol.MethodProperty).GetString()!,
                item.GetProperty(NativeCoverageRf3FixtureProtocol.InstanceProperty).GetString()!)).ToArray();
        if (cases.Length != NativeCoverageRf3Protocol.RequiredContributorCount
            || cases.Distinct().Count() != cases.Length || !HasExactCases(cases))
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidRun);
        }
        return cases;
    }

    internal static bool IsRunIdentity(string runId, string imageReference)
        => Guid.TryParseExact(runId, NativeCoverageRf3Protocol.GuidFormat, out var parsed)
        && parsed.ToString(NativeCoverageRf3Protocol.GuidFormat) == runId
        && imageReference == NativeCoverageRf3FixtureProtocol.CoverageImagePrefix
            + parsed.ToString(NativeCoverageRf3Protocol.GuidCompactFormat);

    private static NativeCoverageRf3ExecutionBounds ExpectedBounds(NativeCoverageExecutionOptions value) => new(
        value.MaximumDescriptorBytes, value.MaximumFiles, value.ReadBufferBytes, value.MaximumTotalBytes,
        value.MaximumFileBytes, value.MaximumPathCharacters, value.MaximumManifestBytes, value.MaximumReportBytes,
        value.ShutdownTimeout.ToString(NativeCoverageRf3RunProtocol.TimeSpanFormat, CultureInfo.InvariantCulture),
        value.SettlementTimeout.ToString(NativeCoverageRf3RunProtocol.TimeSpanFormat, CultureInfo.InvariantCulture),
        value.ContainerStopTimeout.ToString(NativeCoverageRf3RunProtocol.TimeSpanFormat, CultureInfo.InvariantCulture),
        value.ApplicationCleanupTimeout.ToString(NativeCoverageRf3RunProtocol.TimeSpanFormat, CultureInfo.InvariantCulture));

    private static bool HasExactCases(IReadOnlyCollection<NativeCoverageRf3CaseIdentity> cases)
    {
        var expected = new HashSet<NativeCoverageRf3CaseIdentity>
        {
            new(NativeCoverageRf3FixtureProtocol.QueryContributorClass,
                NativeCoverageRf3FixtureProtocol.QueryContributorMethod,
                NativeCoverageRf3FixtureProtocol.QueryContributorMethod),
            new(NativeCoverageRf3FixtureProtocol.CrudContributorClass,
                NativeCoverageRf3FixtureProtocol.CrudCreatePatchDeleteMethod,
                NativeCoverageRf3FixtureProtocol.CrudCreatePatchDeleteMethod),
            new(NativeCoverageRf3FixtureProtocol.CrudContributorClass,
                NativeCoverageRf3FixtureProtocol.CrudPatchDeleteCreateMethod,
                NativeCoverageRf3FixtureProtocol.CrudPatchDeleteCreateMethod),
            new(NativeCoverageRf3FixtureProtocol.CrudContributorClass,
                NativeCoverageRf3FixtureProtocol.CrudStaleReplacementMethod,
                NativeCoverageRf3FixtureProtocol.CrudStaleReplacementMethod)
        };
        return cases.All(expected.Remove) && expected.Count == 0;
    }

}
