using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageClusterFixtureContextArtifactReader
{
    internal static NativeCoverageClusterPreparedContext Read(byte[] bytes, string expectedHash, string sourceHash,
        NativeCoverageClusterExecutionBounds bounds, NativeCoverageToolPackage expectedTool)
    {
        if (NativeCoverageClusterFixtureArtifactValidation.Hash(bytes) != expectedHash)
        {
            throw Invalid();
        }

        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        ValidateRoot(root);
        NativeCoverageClusterContextInventoryReader.ValidateProducerSources(
            root.GetProperty(NativeCoverageClusterFixtureProtocol.ProducerSourcesProperty), bounds);
        var serverElement = root.GetProperty(NativeCoverageClusterFixtureProtocol.ServerProperty);
        var tool = root.GetProperty(NativeCoverageClusterFixtureProtocol.ToolProperty);
        var sourceTemplates = root.GetProperty(NativeCoverageClusterFixtureProtocol.SourceTemplatesProperty);
        var baseImage = root.GetProperty(NativeCoverageClusterFixtureProtocol.BaseImageProperty);
        NativeCoverageClusterContextInventoryReader.ValidateManifestSections(sourceTemplates, baseImage);
        var server = ReadServer(serverElement, sourceHash);
        var collector = ReadCollector(tool, sourceTemplates, expectedTool);
        var inventory = NativeCoverageClusterContextInventoryReader.ReadDockerfileHash(
            root.GetProperty(NativeCoverageClusterFixtureProtocol.FilesProperty), bounds);
        ValidateBounds(root.GetProperty(NativeCoverageClusterFixtureProtocol.BoundsProperty), bounds);
        return new(NativeCoverageClusterFixtureArtifactValidation.RequiredString(root,
                NativeCoverageClusterFixtureProtocol.InvocationIdProperty), server, collector, inventory.Hash,
            NativeCoverageClusterFixtureArtifactValidation.RequiredString(sourceTemplates,
                NativeCoverageClusterFixtureProtocol.DockerfileSha256Property),
            NativeCoverageClusterFixtureArtifactValidation.RequiredString(baseImage,
                NativeCoverageClusterFixtureProtocol.ReferenceProperty),
            NativeCoverageClusterFixtureArtifactValidation.RequiredString(baseImage,
                NativeCoverageClusterFixtureProtocol.SourceReceiptSha256Property), inventory.Count, inventory.TotalBytes,
            bytes.Length);
    }

    private static void ValidateRoot(JsonElement root)
    {
        if (!NativeCoverageClusterFixtureArtifactValidation.HasExactProperties(root,
                NativeCoverageClusterFixtureProtocol.SchemaVersionProperty,
                NativeCoverageClusterFixtureProtocol.InvocationIdProperty,
                NativeCoverageClusterFixtureProtocol.ProducerSourcesProperty,
                NativeCoverageClusterFixtureProtocol.SourceTemplatesProperty,
                NativeCoverageClusterFixtureProtocol.BaseImageProperty,
                NativeCoverageClusterFixtureProtocol.ServerProperty,
                NativeCoverageClusterFixtureProtocol.ToolProperty,
                NativeCoverageClusterFixtureProtocol.BoundsProperty,
                NativeCoverageClusterFixtureProtocol.FilesProperty)
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.SchemaVersionProperty).GetInt32()
                != NativeCoverageClusterProtocol.ContextSchemaVersion
            || root.GetProperty(NativeCoverageClusterFixtureProtocol.ProducerSourcesProperty).ValueKind
                != JsonValueKind.Object)
        {
            throw Invalid();
        }
    }

    private static NativeCoverageClusterServer ReadServer(JsonElement element, string sourceHash)
    {
        var server = new NativeCoverageClusterServer(
            NativeCoverageClusterFixtureArtifactValidation.RequiredString(element,
                NativeCoverageClusterFixtureProtocol.MvidProperty),
            NativeCoverageClusterFixtureArtifactValidation.RequiredString(element,
                NativeCoverageClusterFixtureProtocol.DllSha256Property),
            NativeCoverageClusterFixtureArtifactValidation.RequiredString(element,
                NativeCoverageClusterFixtureProtocol.PdbSha256Property),
            NativeCoverageClusterFixtureArtifactValidation.RequiredString(element,
                NativeCoverageClusterFixtureProtocol.SourceReceiptSha256Property));
        if (!NativeCoverageClusterFixtureArtifactValidation.HasExactProperties(element,
                NativeCoverageClusterFixtureProtocol.DllNameProperty,
                NativeCoverageClusterFixtureProtocol.DllSha256Property,
                NativeCoverageClusterFixtureProtocol.PdbNameProperty,
                NativeCoverageClusterFixtureProtocol.PdbSha256Property,
                NativeCoverageClusterFixtureProtocol.MvidProperty,
                NativeCoverageClusterFixtureProtocol.SourceReceiptSha256Property)
            || server.SourceReceiptSha256 != sourceHash
            || NativeCoverageClusterFixtureArtifactValidation.RequiredString(element,
                NativeCoverageClusterFixtureProtocol.DllNameProperty) != NativeCoverageClusterFixtureProtocol.KeyLoadServerDllName
            || NativeCoverageClusterFixtureArtifactValidation.RequiredString(element,
                NativeCoverageClusterFixtureProtocol.PdbNameProperty) != NativeCoverageClusterFixtureProtocol.KeyLoadServerPdbName
            || !NativeCoverageClusterFixtureArtifactValidation.IsSha256(server.DllSha256)
            || !NativeCoverageClusterFixtureArtifactValidation.IsSha256(server.PdbSha256))
        {
            throw Invalid();
        }
        return server;
    }

    private static NativeCoverageClusterCollector ReadCollector(JsonElement tool, JsonElement sourceTemplates,
        NativeCoverageToolPackage expectedTool)
    {
        var collector = new NativeCoverageClusterCollector(
            NativeCoverageClusterFixtureArtifactValidation.RequiredString(tool,
                NativeCoverageClusterFixtureProtocol.PackageIdProperty),
            NativeCoverageClusterFixtureArtifactValidation.RequiredString(tool,
                NativeCoverageClusterFixtureProtocol.VersionProperty),
            NativeCoverageClusterFixtureArtifactValidation.RequiredString(tool,
                NativeCoverageClusterFixtureProtocol.ClosureDigestProperty),
            NativeCoverageClusterFixtureArtifactValidation.RequiredString(sourceTemplates,
                NativeCoverageClusterFixtureProtocol.SettingsSha256Property));
        if (!NativeCoverageClusterFixtureArtifactValidation.HasExactProperties(tool,
                NativeCoverageClusterFixtureProtocol.PackageIdProperty,
                NativeCoverageClusterFixtureProtocol.VersionProperty,
                NativeCoverageClusterFixtureProtocol.RepositoryCommitProperty,
                NativeCoverageClusterFixtureProtocol.NupkgSha256Property,
                NativeCoverageClusterFixtureProtocol.NupkgSha512Property,
                NativeCoverageClusterFixtureProtocol.ClosureDigestProperty)
            || !NativeCoverageClusterFixtureArtifactValidation.IsSha256(collector.ClosureDigest)
            || !NativeCoverageClusterFixtureArtifactValidation.IsSha256(collector.SettingsSha256)
            || !NativeCoverageClusterFixtureArtifactValidation.IsSha256(
                NativeCoverageClusterFixtureArtifactValidation.RequiredString(tool,
                    NativeCoverageClusterFixtureProtocol.NupkgSha256Property))
            || !NativeCoveragePackageDigestValidation.IsCanonicalSha512(NativeCoverageClusterFixtureArtifactValidation.RequiredString(tool,
                NativeCoverageClusterFixtureProtocol.NupkgSha512Property))
            || collector.PackageId != NativeCoverageClusterFixtureProtocol.CoverageToolPackageId
            || collector.Version != expectedTool.Version)
        {
            throw Invalid();
        }
        return collector;
    }

    private static void ValidateBounds(JsonElement actual, NativeCoverageClusterExecutionBounds expected)
    {
        if (!NativeCoverageClusterFixtureArtifactValidation.HasExactProperties(actual,
                NativeCoverageClusterFixtureProtocol.MaximumFilesProperty,
                NativeCoverageClusterFixtureProtocol.MaximumTotalBytesProperty,
                NativeCoverageClusterFixtureProtocol.MaximumFileBytesProperty,
                NativeCoverageClusterFixtureProtocol.MaximumPathCharactersProperty,
                NativeCoverageClusterFixtureProtocol.MaximumManifestBytesProperty,
                NativeCoverageClusterFixtureProtocol.ReadBufferBytesProperty,
                NativeCoverageClusterFixtureProtocol.ShutdownSecondsProperty,
                NativeCoverageClusterFixtureProtocol.SettlementSecondsProperty,
                NativeCoverageClusterFixtureProtocol.MaximumReportBytesProperty)
            || !NativeCoverageClusterFixtureArtifactValidation.BoundsMatch(actual, expected))
        {
            throw Invalid();
        }
    }

    private static InvalidOperationException Invalid() =>
        NativeCoverageClusterFixtureArtifactValidation.Invalid();
}
