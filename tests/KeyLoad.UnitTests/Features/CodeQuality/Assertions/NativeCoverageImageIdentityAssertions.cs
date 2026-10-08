using System.Text;
using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageImageIdentityAssertions
{
    private const string ScriptDirectory = "scripts/Features/CodeQuality";
    private const string DockerfileTemplate = "functional-coverage.server-image.dockerfile";
    private const string SettingsSource = "functional-coverage.server.settings.xml";
    private const string WrapperSource = "functional-coverage.server-wrapper.sh";
    private const string LifecycleSource = "functional-coverage.server-lifecycle.sh";
    private const string TargetSource = "functional-coverage.server-target.sh";
    private const string IgnoreSource = ".dockerignore";
    private const string Placeholder = "{{SOURCE_BOUND_ASPNET_IMAGE}}";
    private const string NuspecIdPattern = "<id>\\s*([^<]+?)\\s*</id>";
    private const string NuspecVersionPattern = "<version>\\s*([^<]+?)\\s*</version>";
    private const string NuspecPattern = "<repository\\b[^>]*\\bcommit=\"([0-9a-f]{40})\"";

    internal static void Verify(JsonElement manifest, NativeCoverageImageFixture fixture,
        NativeCoverageImageInvocation invocation, NativeCoverageImageExpected expected)
    {
        NativeCoverageImageOracleSupport.RequireKeys(manifest, NativeCoverageImageFields.SchemaVersion, NativeCoverageImageFields.InvocationId,
            NativeCoverageImageFields.ProducerSources, NativeCoverageImageFields.SourceTemplates, NativeCoverageImageFields.BaseImage, NativeCoverageImageFields.Server, NativeCoverageImageFields.Tool, NativeCoverageImageFields.Bounds, NativeCoverageImageFields.Files);
        NativeCoverageImageOracleSupport.Ensure(manifest.GetProperty(NativeCoverageImageFields.SchemaVersion).GetInt32()
            == NativeCoverageImageConstants.SchemaVersion
            && manifest.GetProperty(NativeCoverageImageFields.InvocationId).GetString() == invocation.InvocationId,
            "The context manifest schema or invocation identity changed.");
        VerifyBase(manifest.GetProperty(NativeCoverageImageFields.BaseImage), invocation, fixture);
        VerifyServer(manifest.GetProperty(NativeCoverageImageFields.Server), invocation, fixture.Options.Coverage.Value.MaximumManifestBytes);
        VerifyTool(manifest.GetProperty(NativeCoverageImageFields.Tool), fixture.Tool, expected.ToolClosureDigest,
            fixture.Options.Coverage.Value.MaximumFileBytes, fixture.Options.Coverage.Value.MaximumManifestBytes);
        VerifyBounds(manifest.GetProperty(NativeCoverageImageFields.Bounds), fixture.Options.Coverage.Value);
        VerifyProducerSources(manifest.GetProperty(NativeCoverageImageFields.ProducerSources), fixture.Options.Coverage.Value.MaximumManifestBytes);
        VerifyTemplateSources(manifest.GetProperty(NativeCoverageImageFields.SourceTemplates), fixture);
    }

    private static void VerifyBase(JsonElement baseImage, NativeCoverageImageInvocation invocation,
        NativeCoverageImageFixture fixture)
    {
        NativeCoverageImageOracleSupport.RequireKeys(baseImage, NativeCoverageImageFields.Reference, NativeCoverageImageFields.SourceReceiptSha256);
        NativeCoverageImageOracleSupport.Ensure(baseImage.GetProperty(NativeCoverageImageFields.Reference).GetString() == invocation.BaseReference
            && baseImage.GetProperty(NativeCoverageImageFields.SourceReceiptSha256).GetString()
            == NativeCoverageImageOracleSupport.HashFile(invocation.BaseReceiptPath, fixture.Options.Coverage.Value.MaximumManifestBytes),
            "The manifest does not use the source-pinned ASP.NET base receipt.");
        var sourcePath = Path.Combine(NativeCoverageImageFixture.RepositoryRoot, NativeCoverageImageConstants.Dockerfile);
        using var receipt = JsonDocument.Parse(NativeCoverageImageOracleSupport.ReadBounded(invocation.BaseReceiptPath,
            fixture.Options.Coverage.Value.MaximumManifestBytes));
        var root = receipt.RootElement;
        NativeCoverageImageOracleSupport.RequireKeys(root, NativeCoverageImageFields.SchemaVersion, NativeCoverageImageFields.EvidenceKind,
            NativeCoverageImageFields.SourcePath, NativeCoverageImageFields.SourceSha256, NativeCoverageImageFields.ImageReference);
        NativeCoverageImageOracleSupport.Ensure(root.GetProperty(NativeCoverageImageFields.SchemaVersion).GetInt32()
            == NativeCoverageImageConstants.SchemaVersion
            && root.GetProperty(NativeCoverageImageFields.EvidenceKind).GetString() == NativeCoverageImageConstants.LocalEvidenceKind
            && root.GetProperty(NativeCoverageImageFields.ImageReference).GetString() == invocation.BaseReference
            && root.GetProperty(NativeCoverageImageFields.SourcePath).GetString() == NativeCoverageImageConstants.Dockerfile
            && root.GetProperty(NativeCoverageImageFields.SourceSha256).GetString() == NativeCoverageImageOracleSupport.HashFile(sourcePath, fixture.Options.Coverage.Value.MaximumManifestBytes),
            "The base receipt does not bind the actual Dockerfile source pin and bytes.");
    }

    private static void VerifyServer(JsonElement server, NativeCoverageImageInvocation invocation, int maximumManifestBytes)
    {
        NativeCoverageImageOracleSupport.RequireKeys(server, NativeCoverageImageFields.DllName, NativeCoverageImageFields.DllSha256, NativeCoverageImageFields.PdbName,
            NativeCoverageImageFields.PdbSha256, NativeCoverageImageFields.Mvid, NativeCoverageImageFields.SourceReceiptSha256);
        NativeCoverageImageOracleSupport.Ensure(server.GetProperty(NativeCoverageImageFields.DllName).GetString() == NativeCoverageImageConstants.ServerDll
            && server.GetProperty(NativeCoverageImageFields.PdbName).GetString() == NativeCoverageImageConstants.ServerPdb
            && server.GetProperty(NativeCoverageImageFields.DllSha256).GetString() == invocation.ServerDllSha256
            && server.GetProperty(NativeCoverageImageFields.PdbSha256).GetString() == invocation.ServerPdbSha256
            && server.GetProperty(NativeCoverageImageFields.Mvid).GetString() == invocation.Mvid
            && server.GetProperty(NativeCoverageImageFields.SourceReceiptSha256).GetString()
            == NativeCoverageImageOracleSupport.HashFile(invocation.SourceReceiptPath, maximumManifestBytes),
            "The context Server identity differs from observed Release DLL/PDB/MVID bytes.");
    }

    private static void VerifyTool(JsonElement tool, NativeCoverageToolPackage package,
        string closureDigest, int maximumFileBytes, int maximumManifestBytes)
    {
        NativeCoverageImageOracleSupport.RequireKeys(tool, NativeCoverageImageFields.PackageId, NativeCoverageImageFields.Version, NativeCoverageImageFields.RepositoryCommit,
            NativeCoverageImageFields.NupkgSha256, NativeCoverageImageFields.NupkgSha512, NativeCoverageImageFields.ClosureDigest);
        NativeCoverageImageOracleSupport.Ensure(tool.GetProperty(NativeCoverageImageFields.PackageId).GetString() == NativeCoverageImageConstants.CoveragePackageId
            && tool.GetProperty(NativeCoverageImageFields.Version).GetString() == package.Version,
            "The materialized tool differs from AppHost package metadata.");
        var identity = NativeCoverageImagePackageOracle.ReadArchiveIdentity(package, maximumFileBytes, maximumManifestBytes);
        NativeCoverageImageOracleSupport.Ensure(tool.GetProperty(NativeCoverageImageFields.NupkgSha256).GetString() == identity.Sha256
            && tool.GetProperty(NativeCoverageImageFields.NupkgSha512).GetString() == identity.Sha512
            && tool.GetProperty(NativeCoverageImageFields.ClosureDigest).GetString() == closureDigest,
            "The context tool receipt differs from the restored package archive and closure.");
        NativeCoverageImagePackageDigestAssertions.Verify(tool.GetProperty(NativeCoverageImageFields.NupkgSha512).GetString()!);
        var nuspecPath = Path.Combine(package.PackageRoot,
            $"{NativeCoverageImageConstants.CoveragePackageId}.nuspec");
        var nuspec = Encoding.UTF8.GetString(NativeCoverageImageOracleSupport.ReadBounded(nuspecPath, maximumManifestBytes));
        var id = System.Text.RegularExpressions.Regex.Match(nuspec, NuspecIdPattern).Groups[1].Value;
        var version = System.Text.RegularExpressions.Regex.Match(nuspec, NuspecVersionPattern).Groups[1].Value;
        var commit = System.Text.RegularExpressions.Regex.Match(nuspec, NuspecPattern).Groups[1].Value;
        NativeCoverageImageOracleSupport.Ensure(id == NativeCoverageImageConstants.CoveragePackageId
            && version == package.Version && commit.Length == 40
            && tool.GetProperty(NativeCoverageImageFields.RepositoryCommit).GetString() == commit,
            "The context tool repository identity differs from its restored nuspec.");
    }

    private static void VerifyBounds(JsonElement bounds, NativeCoverageExecutionOptions options)
    {
        NativeCoverageImageOracleSupport.RequireKeys(bounds, NativeCoverageImageFields.MaximumFiles, NativeCoverageImageFields.MaximumTotalBytes,
            NativeCoverageImageFields.MaximumFileBytes, NativeCoverageImageFields.MaximumPathCharacters, NativeCoverageImageFields.MaximumManifestBytes, NativeCoverageImageFields.ReadBufferBytes,
            NativeCoverageImageFields.ShutdownSeconds, NativeCoverageImageFields.SettlementSeconds, NativeCoverageImageFields.MaximumReportBytes);
        NativeCoverageImageOracleSupport.Ensure(bounds.GetProperty(NativeCoverageImageFields.MaximumFiles).GetInt32() == options.MaximumFiles
            && bounds.GetProperty(NativeCoverageImageFields.MaximumTotalBytes).GetInt64() == options.MaximumTotalBytes
            && bounds.GetProperty(NativeCoverageImageFields.MaximumFileBytes).GetInt32() == options.MaximumFileBytes
            && bounds.GetProperty(NativeCoverageImageFields.MaximumPathCharacters).GetInt32() == options.MaximumPathCharacters
            && bounds.GetProperty(NativeCoverageImageFields.MaximumManifestBytes).GetInt32() == options.MaximumManifestBytes
            && bounds.GetProperty(NativeCoverageImageFields.ReadBufferBytes).GetInt32() == options.ReadBufferBytes
            && bounds.GetProperty(NativeCoverageImageFields.ShutdownSeconds).GetInt32() == checked((int)options.ShutdownTimeout.TotalSeconds)
            && bounds.GetProperty(NativeCoverageImageFields.SettlementSeconds).GetInt32() == checked((int)options.SettlementTimeout.TotalSeconds)
            && bounds.GetProperty(NativeCoverageImageFields.MaximumReportBytes).GetInt32() == options.MaximumReportBytes,
            "The context manifest does not preserve the validated native options snapshot.");
    }

    private static void VerifyProducerSources(JsonElement sources, int maximumBytes)
    {
        NativeCoverageImageOracleSupport.RequireKeys(sources, NativeCoverageImageFields.Contracts, NativeCoverageImageFields.Files, NativeCoverageImageFields.Tool, NativeCoverageImageFields.Materializer, NativeCoverageImageFields.Entry);
        VerifySource(sources.GetProperty(NativeCoverageImageFields.Contracts), "functional-coverage.server-image-contracts.mjs", maximumBytes);
        VerifySource(sources.GetProperty(NativeCoverageImageFields.Files), "functional-coverage.server-image-files.mjs", maximumBytes);
        VerifySource(sources.GetProperty(NativeCoverageImageFields.Tool), "functional-coverage.server-image-tool.mjs", maximumBytes);
        VerifySource(sources.GetProperty(NativeCoverageImageFields.Materializer), "functional-coverage.server-image-materializer.mjs", maximumBytes);
        VerifySource(sources.GetProperty(NativeCoverageImageFields.Entry), "functional-coverage.server-image.mjs", maximumBytes);
    }

    private static void VerifyTemplateSources(JsonElement sources, NativeCoverageImageFixture fixture)
    {
        NativeCoverageImageOracleSupport.RequireKeys(sources, NativeCoverageImageFields.DockerfileSha256, NativeCoverageImageFields.DockerfileMode,
            "dockerignoreSha256", "dockerignoreMode", "settingsSha256", "wrapperSha256", "wrapperMode",
            "lifecycleSha256", "lifecycleMode", "targetSha256", "targetMode");
        var directory = Path.Combine(NativeCoverageImageFixture.RepositoryRoot, ScriptDirectory);
        var dockerTemplateBytes = NativeCoverageImageOracleSupport.ReadBounded(
            Path.Combine(directory, DockerfileTemplate), fixture.Options.Coverage.Value.MaximumManifestBytes);
        var dockerTemplate = Encoding.UTF8.GetString(dockerTemplateBytes);
        NativeCoverageImageOracleSupport.Ensure(dockerTemplate.Split(Placeholder, StringSplitOptions.None).Length == 2,
            "The source-owned Dockerfile template does not have one pin placeholder.");
        NativeCoverageImageOracleSupport.Ensure(sources.GetProperty(NativeCoverageImageFields.DockerfileSha256).GetString()
            == NativeCoverageImageOracleSupport.Hash(dockerTemplateBytes)
            && sources.GetProperty(NativeCoverageImageFields.DockerfileMode).GetInt32()
            == NativeCoverageImageOracleSupport.Mode(Path.Combine(directory, DockerfileTemplate)),
            "The source-template receipt does not bind the original Dockerfile bytes and mode.");
        VerifyTemplate(sources, "settingsSha256", null, SettingsSource, fixture);
        VerifyTemplate(sources, "wrapperSha256", "wrapperMode", WrapperSource, fixture);
        VerifyTemplate(sources, "lifecycleSha256", "lifecycleMode", LifecycleSource, fixture);
        VerifyTemplate(sources, "targetSha256", "targetMode", TargetSource, fixture);
        VerifyTemplate(sources, "dockerignoreSha256", "dockerignoreMode", IgnoreSource, fixture);
    }

    private static void VerifySource(JsonElement metadata, string fileName, int maximumBytes)
    {
        var path = Path.Combine(NativeCoverageImageFixture.RepositoryRoot, ScriptDirectory, fileName);
        NativeCoverageImageSourceMetadata.Verify(metadata, path, maximumBytes);
    }

    private static void VerifyTemplate(JsonElement sources, string hashField, string? modeField,
        string fileName, NativeCoverageImageFixture fixture)
    {
        var path = Path.Combine(NativeCoverageImageFixture.RepositoryRoot, ScriptDirectory, fileName);
        NativeCoverageImageOracleSupport.Ensure(sources.GetProperty(hashField).GetString()
            == NativeCoverageImageOracleSupport.HashFile(path, fixture.Options.Coverage.Value.MaximumManifestBytes)
            && (modeField is null || sources.GetProperty(modeField).GetInt32()
                == NativeCoverageImageOracleSupport.Mode(path)),
            $"The source template {fileName} differs from its observed source bytes.");
    }
}
