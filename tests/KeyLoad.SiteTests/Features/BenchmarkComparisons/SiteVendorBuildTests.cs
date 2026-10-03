using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

    /// <summary>Proves vendor compression metadata does not replace exact raw-byte identity.</summary>
internal sealed class SiteVendorBuildTests
{
    /// <summary>Accepts changed historical sizes while reporting independently measured current gzip output.</summary>
    [Test]
    public async Task AC_BC_016_ValidHistoricalGzipMetadataEmitsIndependentRuntimeCompressionReceipt()
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var inputs = fixture.Inputs.Site;
        var token = TestContext.Current!.Execution.CancellationToken;
        var sourceManifestPath = ManifestPath(inputs.Repository);
        var originalManifest = await File.ReadAllBytesAsync(sourceManifestPath, token);
        await using var scope = await SiteVendorTestScope.CreateAsync(fixture, token);
        var manifest = await scope.ReadManifestAsync(token);
        SetAllGzipValues(manifest, JsonValue.Create(SiteVendorTokens.HistoricalGzipSize));
        await scope.WriteManifestAsync(manifest, token);
        var oracleBytes = await scope.RunIndependentGzipOracleAsync(token);
        var build = await SiteIsolatedBuilderProcess.RunAsync(scope.Fixture, scope.Output, token);

        await Assert.That(build.ExitCode).IsEqualTo(SiteTokens.ProcessSuccessExitCode);
        await Assert.That(build.StandardError.Length).IsEqualTo(SiteTokens.Zero);
        using var actual = JsonDocument.Parse(build.StandardOutput);
        using var oracle = JsonDocument.Parse(oracleBytes);
        var receipt = actual.RootElement.GetProperty(SiteVendorTokens.Compression);
        await Assert.That(receipt.GetProperty(SiteVendorTokens.NodeVersion).GetString())
            .IsEqualTo(oracle.RootElement.GetProperty(SiteVendorTokens.NodeVersion).GetString());
        await Assert.That(receipt.GetProperty(SiteVendorTokens.ZlibVersion).GetString())
            .IsEqualTo(oracle.RootElement.GetProperty(SiteVendorTokens.ZlibVersion).GetString());
        await AssertCompressionEntries(receipt.GetProperty(SiteVendorTokens.Vendor),
            oracle.RootElement.GetProperty(SiteVendorTokens.Vendor));
        await AssertExistingBuildReceipt(actual.RootElement, scope.Output, scope.Inputs, token);
        await SiteBuildArtifacts.CompareEmittedAssets(scope.Inputs, scope.Output, token);
        await SiteBuildArtifacts.CompareVendorManifest(scope.Inputs, scope.Output, token);
        await scope.AssertOriginalVendorUnchangedAsync(token);
        await scope.AssertEmittedManifestMatchesCopyAsync(token);
        await Assert.That((await File.ReadAllBytesAsync(sourceManifestPath, token)).SequenceEqual(originalManifest)).IsTrue();
    }

    /// <summary>Rejects each invalid historical gzip value before the builder creates isolated output.</summary>
    [Test]
    public async Task AC_BC_016_InvalidHistoricalGzipMetadataIsRejectedBeforeOutputCreation()
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        JsonNode?[] invalidValues =
        [
            JsonValue.Create(SiteVendorTokens.ZeroGzipBytes),
            JsonValue.Create(SiteVendorTokens.NegativeGzipBytes),
            JsonValue.Create(SiteVendorTokens.FractionalGzipBytes),
            JsonValue.Create(SiteVendorTokens.InvalidGzipText),
            null,
            JsonValue.Create(SiteVendorTokens.UnsafeGzipBytes),
        ];

        foreach (var invalid in invalidValues)
        {
            await AssertRejectedAsync(fixture, manifest => SetAllGzipValues(manifest, invalid?.DeepClone()), token);
        }
    }

    /// <summary>Rejects identity, raw-byte, hash, and recorded-length corruption without leaving output.</summary>
    [Test]
    public async Task AC_BC_016_VendorIdentityAndRawByteCorruptionAreRejectedBeforeOutputCreation()
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        Action<JsonObject>[] metadataMutations =
        [
            manifest => manifest[SiteVendorTokens.ManifestPackage] = SiteVendorTokens.WrongPackage,
            manifest => manifest[SiteVendorTokens.ManifestVersion] = SiteVendorTokens.WrongVersion,
            manifest => manifest[SiteVendorTokens.ManifestLicense] = SiteVendorTokens.WrongLicense,
            manifest => manifest[SiteVendorTokens.ManifestSourceCommit] = SiteVendorTokens.WrongSourceCommit,
            manifest => manifest[SiteVendorTokens.ManifestIntegrity] = SiteVendorTokens.WrongIntegrity,
            manifest => FirstManifestFile(manifest)[SiteVendorTokens.ManifestPath] = SiteVendorTokens.WrongPath,
            manifest => FirstManifestFile(manifest)[SiteVendorTokens.ManifestSha256] = SiteVendorTokens.WrongSha256,
            manifest => FirstManifestFile(manifest)[SiteVendorTokens.ManifestBytes] = SiteVendorTokens.FirstByteIndex,
        ];

        foreach (var mutation in metadataMutations)
        {
            await AssertRejectedAsync(fixture, mutation, token);
        }

        await AssertByteCorruptionRejectedAsync(fixture, token);
    }

    private static async Task AssertCompressionEntries(JsonElement actual, JsonElement oracle)
    {
        await Assert.That(actual.GetArrayLength()).IsEqualTo(SiteVendorTokens.VendorFiles.Length);
        await Assert.That(oracle.GetArrayLength()).IsEqualTo(SiteVendorTokens.VendorFiles.Length);
        for (var index = SiteVendorTokens.FirstFileIndex; index < SiteVendorTokens.VendorFiles.Length; index++)
        {
            var entry = actual[index];
            var independent = oracle[index];
            await Assert.That(entry.GetProperty(SiteVendorTokens.ManifestPath).GetString())
                .IsEqualTo(SiteVendorTokens.VendorFiles[index]);
            await Assert.That(entry.GetProperty(SiteVendorTokens.JsonSha256).GetString())
                .IsEqualTo(independent.GetProperty(SiteVendorTokens.JsonSha256).GetString());
            await Assert.That(entry.GetProperty(SiteVendorTokens.ManifestBytes).GetInt32())
                .IsEqualTo(independent.GetProperty(SiteVendorTokens.ManifestBytes).GetInt32());
            await Assert.That(entry.GetProperty(SiteVendorTokens.RecordedGzipBytesProperty).GetInt32())
                .IsEqualTo(SiteVendorTokens.HistoricalGzipSize);
            await Assert.That(entry.GetProperty(SiteVendorTokens.RuntimeGzipBytes).GetInt32())
                .IsEqualTo(independent.GetProperty(SiteVendorTokens.RuntimeGzipBytes).GetInt32());
        }
    }

    private static async Task AssertExistingBuildReceipt(JsonElement receipt, string output,
        SiteTestInputs inputs, CancellationToken token)
    {
        await Assert.That(receipt.GetProperty(SiteVendorTokens.Output).GetString()).IsEqualTo(output);
        await Assert.That(receipt.GetProperty(SiteVendorTokens.JavascriptGzipBytes).GetInt32()
            <= SiteVendorTokens.AuthoredJavaScriptGzipLimit).IsTrue();
        await Assert.That(receipt.GetProperty(SiteVendorTokens.CssGzipBytes).GetInt32()
            <= SiteVendorTokens.AuthoredCssGzipLimit).IsTrue();
        using var catalog = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(output,
            SiteTokens.DataDirectory, "isolated-catalog.json"), token));
        await Assert.That(catalog.RootElement.GetProperty(SiteTokens.MeasuredSourceRevision).GetString())
            .IsEqualTo(inputs.MeasuredRevision);
        await Assert.That(File.Exists(Path.Combine(output, SiteTokens.DataDirectory, "isolated", "aggregate.json"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(output, SiteTokens.DataDirectory, "isolated", "projection.json"))).IsTrue();
    }

    private static async Task AssertRejectedAsync(SiteIsolatedFixture fixture, Action<JsonObject> mutation,
        CancellationToken token)
    {
        await using var scope = await SiteVendorTestScope.CreateAsync(fixture, token);
        var manifest = await scope.ReadManifestAsync(token);
        mutation(manifest);
        await scope.WriteManifestAsync(manifest, token);
        var build = await SiteIsolatedBuilderProcess.RunAsync(scope.Fixture, scope.Output, token);
        await Assert.That(build.ExitCode != SiteTokens.ProcessSuccessExitCode).IsTrue();
        await Assert.That(build.StandardError.Contains(SiteVendorTokens.VendorError, StringComparison.Ordinal)).IsTrue();
        await Assert.That(Directory.Exists(scope.Output)).IsFalse();
    }

    private static async Task AssertByteCorruptionRejectedAsync(SiteIsolatedFixture fixture, CancellationToken token)
    {
        await using var scope = await SiteVendorTestScope.CreateAsync(fixture, token);
        await CorruptFirstVendorByte(scope, token);
        var build = await SiteIsolatedBuilderProcess.RunAsync(scope.Fixture, scope.Output, token);
        await Assert.That(build.ExitCode != SiteTokens.ProcessSuccessExitCode).IsTrue();
        await Assert.That(build.StandardError.Contains(SiteVendorTokens.VendorError, StringComparison.Ordinal)).IsTrue();
        await Assert.That(Directory.Exists(scope.Output)).IsFalse();
    }

    private static void SetAllGzipValues(JsonObject manifest, JsonNode? value)
    {
        foreach (var file in manifest[SiteVendorTokens.ManifestFiles]!.AsArray())
        {
            file!.AsObject()[SiteVendorTokens.ManifestGzipBytes] = value?.DeepClone();
        }
    }

    private static JsonObject FirstManifestFile(JsonObject manifest)
        => manifest[SiteVendorTokens.ManifestFiles]!.AsArray()[SiteVendorTokens.FirstFileIndex]!.AsObject();

    private static async Task CorruptFirstVendorByte(SiteVendorTestScope scope, CancellationToken token)
    {
        var file = Path.Combine(scope.FeatureSource, SiteAssetTokens.ThreeVendorRelativePath,
            SiteVendorTokens.VendorFiles[SiteVendorTokens.FirstFileIndex]);
        var bytes = await File.ReadAllBytesAsync(file, token);
        bytes[SiteVendorTokens.FirstByteIndex] ^= SiteVendorTokens.ByteChangeMask;
        await File.WriteAllBytesAsync(file, bytes, token);
    }

    private static string ManifestPath(string repository)
        => Path.Combine(repository, SiteVendorTokens.FeatureDirectory,
            SiteAssetTokens.ThreeVendorRelativePath, SiteAssetTokens.ThreeManifestFile);
}
