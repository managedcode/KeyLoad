using System.Text.Json;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageImageContextOracle
{
    internal static void Verify(NativeCoverageImageFixture fixture, NativeCoverageImageInvocation invocation,
        NativeCoverageImageNodeResult result)
    {
        NativeCoverageImageOracleSupport.Ensure(result.ExitCode == 0 && string.IsNullOrEmpty(result.StandardError),
            "The valid Node materializer did not exit cleanly.");
        NativeCoverageImageOracleSupport.Ensure(result.OriginalExitJoined && result.StandardOutputJoined
            && result.StandardErrorJoined && result.ProcessDisposed,
            "The materializer child or one of its readers was not joined.");
        using var output = JsonDocument.Parse(result.StandardOutput);
        var outputRoot = output.RootElement;
        NativeCoverageImageOracleSupport.RequireKeys(outputRoot,
            NativeCoverageImageFields.ContextDirectory, NativeCoverageImageFields.ManifestPath, NativeCoverageImageFields.ManifestSha256, NativeCoverageImageFields.FileCount, NativeCoverageImageFields.TotalBytes);
        NativeCoverageImageOracleSupport.Ensure(outputRoot.GetProperty(NativeCoverageImageFields.ContextDirectory).GetString() == invocation.ContextPath,
            "The materializer returned another context directory.");
        var manifestPath = outputRoot.GetProperty(NativeCoverageImageFields.ManifestPath).GetString()!;
        NativeCoverageImageOracleSupport.Ensure(manifestPath == Path.Combine(invocation.ContextPath,
            NativeCoverageImageConstants.ContextManifest), "The materializer returned a different context manifest path.");
        var manifestBytes = NativeCoverageImageOracleSupport.ReadBounded(manifestPath,
            fixture.Options.Coverage.Value.MaximumManifestBytes);
        NativeCoverageImageOracleSupport.Ensure(NativeCoverageImageOracleSupport.Hash(manifestBytes)
            == outputRoot.GetProperty(NativeCoverageImageFields.ManifestSha256).GetString()
            && NativeCoverageImageOracleSupport.Mode(manifestPath) == NativeCoverageImageConstants.ContextManifestMode
            && NativeCoverageImageOracleSupport.Mode(invocation.ContextPath) == NativeCoverageImageConstants.PrivateDirectoryMode,
            "The materializer result does not bind the private context and manifest bytes/modes.");
        using var manifest = JsonDocument.Parse(manifestBytes,
            new JsonDocumentOptions { MaxDepth = NativeCoverageImageConstants.MaximumJsonDepth });
        VerifyManifest(outputRoot, manifest.RootElement, manifestBytes, fixture, invocation);
    }

    private static void VerifyManifest(JsonElement output, JsonElement manifest, byte[] manifestBytes,
        NativeCoverageImageFixture fixture, NativeCoverageImageInvocation invocation)
    {
        var expected = NativeCoverageImageExpectedFiles.Create(fixture, invocation);
        NativeCoverageImageIdentityAssertions.Verify(manifest, fixture, invocation, expected);
        NativeCoverageImageInventoryAssertions.Verify(manifest.GetProperty(NativeCoverageImageFields.Files), expected.Files,
            invocation.ContextPath, output, manifestBytes, fixture.Options.Coverage.Value);
        NativeCoverageImageInventoryAssertions.VerifySourceReceipts(invocation,
            fixture.Options.Coverage.Value.MaximumManifestBytes);
    }
}
