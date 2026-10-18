using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageClusterFixtureArtifactReader
{
    internal static async Task<NativeCoverageClusterFixtureArtifacts> ReadAsync(string evidence, string runId,
        string sourceManifestPath, string imageReference, string sourceHash, string sourceRevision,
        NativeCoverageClusterExecutionBounds bounds, IOptions<NativeCoverageExecutionOptions> options,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Value.IsValid())
        {
            throw NativeCoverageClusterFixtureArtifactValidation.Invalid();
        }
        var runDirectory = Path.GetDirectoryName(Path.Combine(evidence,
            NativeCoverageClusterFixtureProtocol.RunManifestName))!;
        var materializerPath = Path.Combine(runDirectory, NativeCoverageClusterFixtureProtocol.MaterializerReceiptName);
        var inspectPath = Path.Combine(runDirectory, NativeCoverageClusterFixtureProtocol.ImageIdReceiptName);
        var invocationPath = Path.Combine(runDirectory, NativeCoverageClusterFixtureProtocol.InvocationName);
        var baseReceiptPath = Path.Combine(runDirectory, NativeCoverageClusterFixtureProtocol.BaseImageReceiptName);
        var materializerBytes = await NativeCoverageClusterBoundedFileReader.ReadAsync(materializerPath,
            NativeCoverageClusterFixtureProtocol.MaximumReceiptBytes, options, token).ConfigureAwait(false);
        var inspectBytes = await NativeCoverageClusterBoundedFileReader.ReadAsync(inspectPath,
            NativeCoverageClusterFixtureProtocol.MaximumImageOutputBytes, options, token).ConfigureAwait(false);
        var invocationBytes = await NativeCoverageClusterBoundedFileReader.ReadAsync(invocationPath,
            bounds.MaximumDescriptorBytes, options, token).ConfigureAwait(false);
        var baseReceiptBytes = await NativeCoverageClusterBoundedFileReader.ReadAsync(baseReceiptPath,
            bounds.MaximumManifestBytes, options, token).ConfigureAwait(false);
        var materializer = NativeCoverageClusterFixtureArtifactValidation.ReadMaterializer(materializerBytes, evidence,
            materializerPath);
        var contextPath = Path.Combine(materializer.Directory,
            NativeCoverageClusterFixtureProtocol.ContextManifestHashFilePath);
        var contextBytes = await NativeCoverageClusterBoundedFileReader.ReadAsync(contextPath,
            bounds.MaximumManifestBytes, options, token).ConfigureAwait(false);
        var contextHash = NativeCoverageClusterFixtureArtifactValidation.Hash(contextBytes);
        var toolPackage = NativeCoverageToolPackage.Read();
        var context = NativeCoverageClusterFixtureContextArtifactReader.Read(contextBytes, contextHash, sourceHash,
            bounds, toolPackage);
        var templatePath = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName,
            NativeCoverageClusterTemplateIdentity.SourceRelativePath);
        var templateBytes = await NativeCoverageClusterBoundedFileReader.ReadAsync(templatePath,
            bounds.MaximumManifestBytes, options, token).ConfigureAwait(false);
        var templateHash = NativeCoverageClusterTemplateIdentity.ReadAndRequire(contextBytes, templateBytes);
        var baseReceipt = NativeCoverageClusterFixtureArtifactValidation.ReadBaseReceipt(baseReceiptBytes, sourceRevision,
            context.BaseImageReference);
        NativeCoverageClusterFixtureArtifactValidation.ValidatePreparation(materializer,
            context, baseReceipt, baseReceiptBytes, contextHash, templateHash, evidence, materializerPath);
        var invocationId = NativeCoverageClusterFixtureInvocationValidation.Validate(invocationBytes,
            materializer.Directory, runId,
            imageReference, sourceHash, sourceManifestPath, baseReceiptPath, baseReceiptBytes, context, bounds,
            toolPackage);
        if (invocationId != context.InvocationId)
        {
            throw NativeCoverageClusterFixtureArtifactValidation.Invalid();
        }
        var imageId = NativeCoverageClusterFixtureArtifactValidation.ReadImageId(inspectBytes);
        return new(materializer.Directory, contextHash, context.DockerfileSha256, imageId, context.Server,
            context.Collector, baseReceipt.ImageReference, NativeCoverageClusterFixtureArtifactValidation.Hash(baseReceiptBytes));
    }
}
