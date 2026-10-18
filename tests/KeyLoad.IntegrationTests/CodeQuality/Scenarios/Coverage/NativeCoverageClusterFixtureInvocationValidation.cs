using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageClusterFixtureInvocationValidation
{
    internal static string Validate(byte[] bytes, string contextDirectory, string runId, string imageReference, string sourceHash,
        string sourceManifestPath, string baseReceiptPath, byte[] baseReceiptBytes,
        NativeCoverageClusterPreparedContext context, NativeCoverageClusterExecutionBounds bounds,
        NativeCoverageToolPackage expectedTool)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (!HasExactProperties(root, NativeCoverageClusterFixtureProtocol.SchemaVersionProperty,
                NativeCoverageClusterFixtureProtocol.InvocationIdProperty,
                NativeCoverageClusterFixtureProtocol.ContextDirectoryProperty,
                NativeCoverageClusterFixtureProtocol.ServerPublishDirectoryProperty,
                NativeCoverageClusterFixtureProtocol.ServerProperty, NativeCoverageClusterFixtureProtocol.BaseImageProperty,
                NativeCoverageClusterFixtureProtocol.ToolProperty, NativeCoverageClusterFixtureProtocol.BoundsProperty)
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.SchemaVersionProperty).GetInt32()
                != NativeCoverageClusterProtocol.InvocationSchemaVersion)
        {
            throw NativeCoverageClusterFixtureArtifactValidation.Invalid();
        }
        var server = root.GetProperty(NativeCoverageClusterFixtureProtocol.ServerProperty);
        var baseImage = root.GetProperty(NativeCoverageClusterFixtureProtocol.BaseImageProperty);
        var tool = root.GetProperty(NativeCoverageClusterFixtureProtocol.ToolProperty);
        var actualBounds = root.GetProperty(NativeCoverageClusterFixtureProtocol.BoundsProperty);
        if (!HasExactProperties(server, NativeCoverageClusterFixtureProtocol.DllSha256Property,
                NativeCoverageClusterFixtureProtocol.PdbSha256Property, NativeCoverageClusterFixtureProtocol.MvidProperty,
                NativeCoverageClusterFixtureProtocol.SourceReceiptPathProperty,
                NativeCoverageClusterFixtureProtocol.SourceReceiptSha256Property)
            || !HasExactProperties(baseImage, NativeCoverageClusterFixtureProtocol.ReferenceProperty,
                NativeCoverageClusterFixtureProtocol.SourceReceiptPathProperty,
                NativeCoverageClusterFixtureProtocol.SourceReceiptSha256Property)
            || !HasExactProperties(tool, NativeCoverageClusterFixtureProtocol.VersionProperty,
                NativeCoverageClusterFixtureProtocol.PackageRootProperty)
            || !SamePath(RequiredString(root, NativeCoverageClusterFixtureProtocol.ContextDirectoryProperty),
                contextDirectory)
            || !Path.IsPathFullyQualified(RequiredString(root,
                NativeCoverageClusterFixtureProtocol.ServerPublishDirectoryProperty))
            || !MatchesServer(server, context.Server, sourceHash, sourceManifestPath)
            || !MatchesBaseImage(baseImage, context, baseReceiptPath, baseReceiptBytes)
            || tool.GetProperty(NativeCoverageClusterFixtureProtocol.VersionProperty).GetString() != context.Collector.Version
            || !SamePath(RequiredString(tool, NativeCoverageClusterFixtureProtocol.PackageRootProperty),
                expectedTool.PackageRoot)
            || imageReference != NativeCoverageClusterFixtureProtocol.CoverageImagePrefix
                + CompactRunId(runId)
            || !IsInvocationId(RequiredString(root, NativeCoverageClusterFixtureProtocol.InvocationIdProperty))
            || !MatchesBounds(actualBounds, bounds))
        {
            throw NativeCoverageClusterFixtureArtifactValidation.Invalid();
        }
        return RequiredString(root, NativeCoverageClusterFixtureProtocol.InvocationIdProperty);
    }

    private static bool MatchesBounds(JsonElement actual, NativeCoverageClusterExecutionBounds expected)
        => NativeCoverageClusterFixtureArtifactValidation.HasExactProperties(actual,
            NativeCoverageClusterFixtureProtocol.MaximumFilesProperty,
            NativeCoverageClusterFixtureProtocol.MaximumTotalBytesProperty,
            NativeCoverageClusterFixtureProtocol.MaximumFileBytesProperty,
            NativeCoverageClusterFixtureProtocol.MaximumPathCharactersProperty,
            NativeCoverageClusterFixtureProtocol.MaximumManifestBytesProperty,
            NativeCoverageClusterFixtureProtocol.ReadBufferBytesProperty,
            NativeCoverageClusterFixtureProtocol.ShutdownSecondsProperty,
            NativeCoverageClusterFixtureProtocol.SettlementSecondsProperty,
            NativeCoverageClusterFixtureProtocol.MaximumReportBytesProperty)
        && NativeCoverageClusterFixtureArtifactValidation.BoundsMatch(actual, expected);

    private static bool MatchesServer(JsonElement server, NativeCoverageClusterServer expected, string sourceHash,
        string sourceManifestPath)
        => server.GetProperty(NativeCoverageClusterFixtureProtocol.SourceReceiptSha256Property).GetString() == sourceHash
        && SamePath(RequiredString(server, NativeCoverageClusterFixtureProtocol.SourceReceiptPathProperty), sourceManifestPath)
        && server.GetProperty(NativeCoverageClusterFixtureProtocol.DllSha256Property).GetString() == expected.DllSha256
        && server.GetProperty(NativeCoverageClusterFixtureProtocol.PdbSha256Property).GetString() == expected.PdbSha256
        && server.GetProperty(NativeCoverageClusterFixtureProtocol.MvidProperty).GetString() == expected.Mvid;

    private static bool MatchesBaseImage(JsonElement baseImage, NativeCoverageClusterPreparedContext expected,
        string receiptPath, byte[] receiptBytes)
        => baseImage.GetProperty(NativeCoverageClusterFixtureProtocol.ReferenceProperty).GetString()
                == expected.BaseImageReference
        && SamePath(RequiredString(baseImage, NativeCoverageClusterFixtureProtocol.SourceReceiptPathProperty), receiptPath)
        && baseImage.GetProperty(NativeCoverageClusterFixtureProtocol.SourceReceiptSha256Property).GetString()
            == NativeCoverageClusterFixtureArtifactValidation.Hash(receiptBytes)
        && expected.BaseImageReceiptSha256 == NativeCoverageClusterFixtureArtifactValidation.Hash(receiptBytes);

    private static string CompactRunId(string runId)
    {
        if (!Guid.TryParseExact(runId, NativeCoverageClusterProtocol.GuidFormat, out var parsed)
            || parsed.ToString(NativeCoverageClusterProtocol.GuidFormat) != runId)
        {
            throw NativeCoverageClusterFixtureArtifactValidation.Invalid();
        }
        return parsed.ToString(NativeCoverageClusterProtocol.GuidCompactFormat);
    }

    private static bool IsInvocationId(string value)
        => Guid.TryParseExact(value, NativeCoverageClusterProtocol.GuidFormat, out var parsed)
            && parsed.ToString(NativeCoverageClusterProtocol.GuidFormat) == value;

    private static bool SamePath(string actual, string expected)
        => Path.IsPathFullyQualified(actual)
            && Path.GetFullPath(actual) == Path.GetFullPath(expected);

    private static bool HasExactProperties(JsonElement element, params string[] names)
        => NativeCoverageClusterFixtureArtifactValidation.HasExactProperties(element, names);

    private static string RequiredString(JsonElement element, string property)
        => NativeCoverageClusterFixtureArtifactValidation.RequiredString(element, property);
}
