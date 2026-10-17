using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageRf3FixtureContextArtifactReader
{
    internal static NativeCoverageRf3PreparedContext Read(byte[] bytes, string expectedHash, string sourceHash,
        NativeCoverageRf3ExecutionBounds bounds, NativeCoverageToolPackage expectedTool)
    {
        if (NativeCoverageRf3FixtureArtifactValidation.Hash(bytes) != expectedHash)
        {
            throw Invalid();
        }

        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        ValidateRoot(root);
        NativeCoverageRf3ContextInventoryReader.ValidateProducerSources(
            root.GetProperty(NativeCoverageRf3FixtureProtocol.ProducerSourcesProperty), bounds);
        var serverElement = root.GetProperty(NativeCoverageRf3FixtureProtocol.ServerProperty);
        var tool = root.GetProperty(NativeCoverageRf3FixtureProtocol.ToolProperty);
        var sourceTemplates = root.GetProperty(NativeCoverageRf3FixtureProtocol.SourceTemplatesProperty);
        var baseImage = root.GetProperty(NativeCoverageRf3FixtureProtocol.BaseImageProperty);
        NativeCoverageRf3ContextInventoryReader.ValidateManifestSections(sourceTemplates, baseImage);
        var server = ReadServer(serverElement, sourceHash);
        var collector = ReadCollector(tool, sourceTemplates, expectedTool);
        var inventory = NativeCoverageRf3ContextInventoryReader.ReadDockerfileHash(
            root.GetProperty(NativeCoverageRf3FixtureProtocol.FilesProperty), bounds);
        ValidateBounds(root.GetProperty(NativeCoverageRf3FixtureProtocol.BoundsProperty), bounds);
        return new(NativeCoverageRf3FixtureArtifactValidation.RequiredString(root,
                NativeCoverageRf3FixtureProtocol.InvocationIdProperty), server, collector, inventory.Hash,
            NativeCoverageRf3FixtureArtifactValidation.RequiredString(sourceTemplates,
                NativeCoverageRf3FixtureProtocol.DockerfileSha256Property),
            NativeCoverageRf3FixtureArtifactValidation.RequiredString(baseImage,
                NativeCoverageRf3FixtureProtocol.ReferenceProperty),
            NativeCoverageRf3FixtureArtifactValidation.RequiredString(baseImage,
                NativeCoverageRf3FixtureProtocol.SourceReceiptSha256Property), inventory.Count, inventory.TotalBytes,
            bytes.Length);
    }

    private static void ValidateRoot(JsonElement root)
    {
        if (!NativeCoverageRf3FixtureArtifactValidation.HasExactProperties(root,
                NativeCoverageRf3FixtureProtocol.SchemaVersionProperty,
                NativeCoverageRf3FixtureProtocol.InvocationIdProperty,
                NativeCoverageRf3FixtureProtocol.ProducerSourcesProperty,
                NativeCoverageRf3FixtureProtocol.SourceTemplatesProperty,
                NativeCoverageRf3FixtureProtocol.BaseImageProperty,
                NativeCoverageRf3FixtureProtocol.ServerProperty,
                NativeCoverageRf3FixtureProtocol.ToolProperty,
                NativeCoverageRf3FixtureProtocol.BoundsProperty,
                NativeCoverageRf3FixtureProtocol.FilesProperty)
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.SchemaVersionProperty).GetInt32()
                != NativeCoverageRf3Protocol.ContextSchemaVersion
            || root.GetProperty(NativeCoverageRf3FixtureProtocol.ProducerSourcesProperty).ValueKind
                != JsonValueKind.Object)
        {
            throw Invalid();
        }
    }

    private static NativeCoverageRf3Server ReadServer(JsonElement element, string sourceHash)
    {
        var server = new NativeCoverageRf3Server(
            NativeCoverageRf3FixtureArtifactValidation.RequiredString(element,
                NativeCoverageRf3FixtureProtocol.MvidProperty),
            NativeCoverageRf3FixtureArtifactValidation.RequiredString(element,
                NativeCoverageRf3FixtureProtocol.DllSha256Property),
            NativeCoverageRf3FixtureArtifactValidation.RequiredString(element,
                NativeCoverageRf3FixtureProtocol.PdbSha256Property),
            NativeCoverageRf3FixtureArtifactValidation.RequiredString(element,
                NativeCoverageRf3FixtureProtocol.SourceReceiptSha256Property));
        if (!NativeCoverageRf3FixtureArtifactValidation.HasExactProperties(element,
                NativeCoverageRf3FixtureProtocol.DllNameProperty,
                NativeCoverageRf3FixtureProtocol.DllSha256Property,
                NativeCoverageRf3FixtureProtocol.PdbNameProperty,
                NativeCoverageRf3FixtureProtocol.PdbSha256Property,
                NativeCoverageRf3FixtureProtocol.MvidProperty,
                NativeCoverageRf3FixtureProtocol.SourceReceiptSha256Property)
            || server.SourceReceiptSha256 != sourceHash
            || NativeCoverageRf3FixtureArtifactValidation.RequiredString(element,
                NativeCoverageRf3FixtureProtocol.DllNameProperty) != NativeCoverageRf3FixtureProtocol.KeyLoadServerDllName
            || NativeCoverageRf3FixtureArtifactValidation.RequiredString(element,
                NativeCoverageRf3FixtureProtocol.PdbNameProperty) != NativeCoverageRf3FixtureProtocol.KeyLoadServerPdbName
            || !NativeCoverageRf3FixtureArtifactValidation.IsSha256(server.DllSha256)
            || !NativeCoverageRf3FixtureArtifactValidation.IsSha256(server.PdbSha256))
        {
            throw Invalid();
        }
        return server;
    }

    private static NativeCoverageRf3Collector ReadCollector(JsonElement tool, JsonElement sourceTemplates,
        NativeCoverageToolPackage expectedTool)
    {
        var collector = new NativeCoverageRf3Collector(
            NativeCoverageRf3FixtureArtifactValidation.RequiredString(tool,
                NativeCoverageRf3FixtureProtocol.PackageIdProperty),
            NativeCoverageRf3FixtureArtifactValidation.RequiredString(tool,
                NativeCoverageRf3FixtureProtocol.VersionProperty),
            NativeCoverageRf3FixtureArtifactValidation.RequiredString(tool,
                NativeCoverageRf3FixtureProtocol.ClosureDigestProperty),
            NativeCoverageRf3FixtureArtifactValidation.RequiredString(sourceTemplates,
                NativeCoverageRf3FixtureProtocol.SettingsSha256Property));
        if (!NativeCoverageRf3FixtureArtifactValidation.HasExactProperties(tool,
                NativeCoverageRf3FixtureProtocol.PackageIdProperty,
                NativeCoverageRf3FixtureProtocol.VersionProperty,
                NativeCoverageRf3FixtureProtocol.RepositoryCommitProperty,
                NativeCoverageRf3FixtureProtocol.NupkgSha256Property,
                NativeCoverageRf3FixtureProtocol.NupkgSha512Property,
                NativeCoverageRf3FixtureProtocol.ClosureDigestProperty)
            || !NativeCoverageRf3FixtureArtifactValidation.IsSha256(collector.ClosureDigest)
            || !NativeCoverageRf3FixtureArtifactValidation.IsSha256(collector.SettingsSha256)
            || !NativeCoverageRf3FixtureArtifactValidation.IsSha256(
                NativeCoverageRf3FixtureArtifactValidation.RequiredString(tool,
                    NativeCoverageRf3FixtureProtocol.NupkgSha256Property))
            || !NativeCoveragePackageDigestValidation.IsCanonicalSha512(NativeCoverageRf3FixtureArtifactValidation.RequiredString(tool,
                NativeCoverageRf3FixtureProtocol.NupkgSha512Property))
            || collector.PackageId != NativeCoverageRf3FixtureProtocol.CoverageToolPackageId
            || collector.Version != expectedTool.Version)
        {
            throw Invalid();
        }
        return collector;
    }

    private static void ValidateBounds(JsonElement actual, NativeCoverageRf3ExecutionBounds expected)
    {
        if (!NativeCoverageRf3FixtureArtifactValidation.HasExactProperties(actual,
                NativeCoverageRf3FixtureProtocol.MaximumFilesProperty,
                NativeCoverageRf3FixtureProtocol.MaximumTotalBytesProperty,
                NativeCoverageRf3FixtureProtocol.MaximumFileBytesProperty,
                NativeCoverageRf3FixtureProtocol.MaximumPathCharactersProperty,
                NativeCoverageRf3FixtureProtocol.MaximumManifestBytesProperty,
                NativeCoverageRf3FixtureProtocol.ReadBufferBytesProperty,
                NativeCoverageRf3FixtureProtocol.ShutdownSecondsProperty,
                NativeCoverageRf3FixtureProtocol.SettlementSecondsProperty,
                NativeCoverageRf3FixtureProtocol.MaximumReportBytesProperty)
            || !NativeCoverageRf3FixtureArtifactValidation.BoundsMatch(actual, expected))
        {
            throw Invalid();
        }
    }

    private static InvalidOperationException Invalid() =>
        NativeCoverageRf3FixtureArtifactValidation.Invalid();
}
