using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageImagePreparationFlow
{
    private const string WrongTemplateHash = "0000000000000000000000000000000000000000000000000000000000000000";

    internal static async Task VerifyTamperAndHealthyAsync(NativeCoverageImageFixture fixture,
        NativeCoverageImageInvocation invocation, NativeCoverageImageNodeResult result)
    {
        var limits = fixture.Options.Coverage.Value;
        var token = TestContext.Current!.Execution.CancellationToken;
        var manifestPath = Path.Combine(invocation.ContextPath, NativeCoverageImageConstants.ContextManifest);
        var original = NativeCoverageImageOracleSupport.ReadBounded(manifestPath, limits.MaximumManifestBytes);
        var snapshot = NativeCoverageImageSourceSnapshot.Capture(invocation.ContextPath, limits.MaximumFiles,
            limits.MaximumFileBytes, limits.MaximumTotalBytes, limits.ReadBufferBytes);
        RequireSelectedTemplate(original, limits.MaximumManifestBytes);
        var changed = JsonNode.Parse(original)!;
        changed[NativeCoverageImageFields.SourceTemplates]![NativeCoverageImageFields.DockerfileSha256] = WrongTemplateHash;
        var tampered = JsonSerializer.SerializeToUtf8Bytes(changed);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await File.WriteAllBytesAsync(manifestPath, tampered, token).ConfigureAwait(false);
            var observed = NativeCoverageImageOracleSupport.ReadBounded(manifestPath, limits.MaximumManifestBytes);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            {
                RequireSelectedTemplate(observed, limits.MaximumManifestBytes);
                return Task.CompletedTask;
            });
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            {
                NativeCoverageImageContextOracle.Verify(fixture, invocation, result);
                return Task.CompletedTask;
            });
            await Assert.That(NativeCoverageImageOracleSupport.ReadBounded(manifestPath,
                limits.MaximumManifestBytes).AsSpan().SequenceEqual(tampered)).IsTrue();
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(manifestPath, original,
            CancellationToken.None), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            snapshot.VerifyUnchanged(invocation.ContextPath, limits.MaximumFiles, limits.MaximumFileBytes,
                limits.MaximumTotalBytes, limits.ReadBufferBytes);
            RequireSelectedTemplate(NativeCoverageImageOracleSupport.ReadBounded(manifestPath,
                limits.MaximumManifestBytes), limits.MaximumManifestBytes);
            NativeCoverageImageContextOracle.Verify(fixture, invocation, result);
            await Assert.That(NativeCoverageImageOracleSupport.ReadBounded(manifestPath,
                limits.MaximumManifestBytes).AsSpan().SequenceEqual(original)).IsTrue();
        }, failures).ConfigureAwait(false);
        NativeCoverageImageNodeSettlement.ThrowFailures(failures);
    }

    private static void RequireSelectedTemplate(byte[] contextBytes, int maximumBytes)
    {
        var path = Path.Combine(NativeCoverageImageFixture.RepositoryRoot,
            NativeCoverageRf3TemplateIdentity.SourceRelativePath);
        var selected = NativeCoverageImageOracleSupport.ReadBounded(path, maximumBytes);
        NativeCoverageRf3TemplateIdentity.ReadAndRequire(contextBytes, selected);
    }
}
