using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageRf3FixtureInvocationValidation
{
    internal static string Validate(byte[] bytes, string contextDirectory, string runId, string imageReference, string sourceHash,
        string sourceManifestPath, string baseReceiptPath, byte[] baseReceiptBytes,
        NativeCoverageRf3PreparedContext context, NativeCoverageRf3ExecutionBounds bounds,
        NativeCoverageToolPackage expectedTool)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (!HasExactProperties(root, NativeCoverageRf3FixtureProtocol.SchemaVersionProperty,
                NativeCoverageRf3FixtureProtocol.InvocationIdProperty,
                NativeCoverageRf3FixtureProtocol.ContextDirectoryProperty,
                NativeCoverageRf3FixtureProtocol.ServerPublishDirectoryProperty,
                NativeCoverageRf3FixtureProtocol.ServerProperty, NativeCoverageRf3FixtureProtocol.BaseImageProperty,
                NativeCoverageRf3FixtureProtocol.ToolProperty, NativeCoverageRf3FixtureProtocol.BoundsProperty)
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.SchemaVersionProperty).GetInt32()
                != NativeCoverageRf3Protocol.InvocationSchemaVersion)
        {
            throw NativeCoverageRf3FixtureArtifactValidation.Invalid();
        }
        var server = root.GetProperty(NativeCoverageRf3FixtureProtocol.ServerProperty);
        var baseImage = root.GetProperty(NativeCoverageRf3FixtureProtocol.BaseImageProperty);
        var tool = root.GetProperty(NativeCoverageRf3FixtureProtocol.ToolProperty);
        var actualBounds = root.GetProperty(NativeCoverageRf3FixtureProtocol.BoundsProperty);
        if (!HasExactProperties(server, NativeCoverageRf3FixtureProtocol.DllSha256Property,
                NativeCoverageRf3FixtureProtocol.PdbSha256Property, NativeCoverageRf3FixtureProtocol.MvidProperty,
                NativeCoverageRf3FixtureProtocol.SourceReceiptPathProperty,
                NativeCoverageRf3FixtureProtocol.SourceReceiptSha256Property)
            || !HasExactProperties(baseImage, NativeCoverageRf3FixtureProtocol.ReferenceProperty,
                NativeCoverageRf3FixtureProtocol.SourceReceiptPathProperty,
                NativeCoverageRf3FixtureProtocol.SourceReceiptSha256Property)
            || !HasExactProperties(tool, NativeCoverageRf3FixtureProtocol.VersionProperty,
                NativeCoverageRf3FixtureProtocol.PackageRootProperty)
            || !SamePath(RequiredString(root, NativeCoverageRf3FixtureProtocol.ContextDirectoryProperty),
                contextDirectory)
            || !Path.IsPathFullyQualified(RequiredString(root,
                NativeCoverageRf3FixtureProtocol.ServerPublishDirectoryProperty))
            || !MatchesServer(server, context.Server, sourceHash, sourceManifestPath)
            || !MatchesBaseImage(baseImage, context, baseReceiptPath, baseReceiptBytes)
            || tool.GetProperty(NativeCoverageRf3FixtureProtocol.VersionProperty).GetString() != context.Collector.Version
            || !SamePath(RequiredString(tool, NativeCoverageRf3FixtureProtocol.PackageRootProperty),
                expectedTool.PackageRoot)
            || imageReference != NativeCoverageRf3FixtureProtocol.CoverageImagePrefix
                + CompactRunId(runId)
            || !IsInvocationId(RequiredString(root, NativeCoverageRf3FixtureProtocol.InvocationIdProperty))
            || !MatchesBounds(actualBounds, bounds))
        {
            throw NativeCoverageRf3FixtureArtifactValidation.Invalid();
        }
        return RequiredString(root, NativeCoverageRf3FixtureProtocol.InvocationIdProperty);
    }

    private static bool MatchesBounds(JsonElement actual, NativeCoverageRf3ExecutionBounds expected)
        => NativeCoverageRf3FixtureArtifactValidation.HasExactProperties(actual,
            NativeCoverageRf3FixtureProtocol.MaximumFilesProperty,
            NativeCoverageRf3FixtureProtocol.MaximumTotalBytesProperty,
            NativeCoverageRf3FixtureProtocol.MaximumFileBytesProperty,
            NativeCoverageRf3FixtureProtocol.MaximumPathCharactersProperty,
            NativeCoverageRf3FixtureProtocol.MaximumManifestBytesProperty,
            NativeCoverageRf3FixtureProtocol.ReadBufferBytesProperty,
            NativeCoverageRf3FixtureProtocol.ShutdownSecondsProperty,
            NativeCoverageRf3FixtureProtocol.SettlementSecondsProperty,
            NativeCoverageRf3FixtureProtocol.MaximumReportBytesProperty)
        && NativeCoverageRf3FixtureArtifactValidation.BoundsMatch(actual, expected);

    private static bool MatchesServer(JsonElement server, NativeCoverageRf3Server expected, string sourceHash,
        string sourceManifestPath)
        => server.GetProperty(NativeCoverageRf3FixtureProtocol.SourceReceiptSha256Property).GetString() == sourceHash
        && SamePath(RequiredString(server, NativeCoverageRf3FixtureProtocol.SourceReceiptPathProperty), sourceManifestPath)
        && server.GetProperty(NativeCoverageRf3FixtureProtocol.DllSha256Property).GetString() == expected.DllSha256
        && server.GetProperty(NativeCoverageRf3FixtureProtocol.PdbSha256Property).GetString() == expected.PdbSha256
        && server.GetProperty(NativeCoverageRf3FixtureProtocol.MvidProperty).GetString() == expected.Mvid;

    private static bool MatchesBaseImage(JsonElement baseImage, NativeCoverageRf3PreparedContext expected,
        string receiptPath, byte[] receiptBytes)
        => baseImage.GetProperty(NativeCoverageRf3FixtureProtocol.ReferenceProperty).GetString()
                == expected.BaseImageReference
        && SamePath(RequiredString(baseImage, NativeCoverageRf3FixtureProtocol.SourceReceiptPathProperty), receiptPath)
        && baseImage.GetProperty(NativeCoverageRf3FixtureProtocol.SourceReceiptSha256Property).GetString()
            == NativeCoverageRf3FixtureArtifactValidation.Hash(receiptBytes)
        && expected.BaseImageReceiptSha256 == NativeCoverageRf3FixtureArtifactValidation.Hash(receiptBytes);

    private static string CompactRunId(string runId)
    {
        if (!Guid.TryParseExact(runId, NativeCoverageRf3Protocol.GuidFormat, out var parsed)
            || parsed.ToString(NativeCoverageRf3Protocol.GuidFormat) != runId)
        {
            throw NativeCoverageRf3FixtureArtifactValidation.Invalid();
        }
        return parsed.ToString(NativeCoverageRf3Protocol.GuidCompactFormat);
    }

    private static bool IsInvocationId(string value)
        => Guid.TryParseExact(value, NativeCoverageRf3Protocol.GuidFormat, out var parsed)
            && parsed.ToString(NativeCoverageRf3Protocol.GuidFormat) == value;

    private static bool SamePath(string actual, string expected)
        => Path.IsPathFullyQualified(actual)
            && Path.GetFullPath(actual) == Path.GetFullPath(expected);

    private static bool HasExactProperties(JsonElement element, params string[] names)
        => NativeCoverageRf3FixtureArtifactValidation.HasExactProperties(element, names);

    private static string RequiredString(JsonElement element, string property)
        => NativeCoverageRf3FixtureArtifactValidation.RequiredString(element, property);
}
