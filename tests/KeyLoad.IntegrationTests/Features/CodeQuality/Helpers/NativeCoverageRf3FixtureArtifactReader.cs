using KeyLoad.AppHost.Features.CodeQuality;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageRf3FixtureArtifactReader
{
    internal static async Task<NativeCoverageRf3FixtureArtifacts> ReadAsync(string evidence, string runId,
        string sourceManifestPath, string imageReference, string sourceHash, string sourceRevision,
        NativeCoverageRf3ExecutionBounds bounds, IOptions<NativeCoverageExecutionOptions> options,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Value.IsValid())
        {
            throw NativeCoverageRf3FixtureArtifactValidation.Invalid();
        }
        var runDirectory = Path.GetDirectoryName(Path.Combine(evidence,
            NativeCoverageRf3FixtureProtocol.RunManifestName))!;
        var materializerPath = Path.Combine(runDirectory, NativeCoverageRf3FixtureProtocol.MaterializerReceiptName);
        var inspectPath = Path.Combine(runDirectory, NativeCoverageRf3FixtureProtocol.ImageIdReceiptName);
        var invocationPath = Path.Combine(runDirectory, NativeCoverageRf3FixtureProtocol.InvocationName);
        var baseReceiptPath = Path.Combine(runDirectory, NativeCoverageRf3FixtureProtocol.BaseImageReceiptName);
        var materializerBytes = await NativeCoverageRf3BoundedFileReader.ReadAsync(materializerPath,
            NativeCoverageRf3FixtureProtocol.MaximumReceiptBytes, options, token).ConfigureAwait(false);
        var inspectBytes = await NativeCoverageRf3BoundedFileReader.ReadAsync(inspectPath,
            NativeCoverageRf3FixtureProtocol.MaximumImageOutputBytes, options, token).ConfigureAwait(false);
        var invocationBytes = await NativeCoverageRf3BoundedFileReader.ReadAsync(invocationPath,
            bounds.MaximumDescriptorBytes, options, token).ConfigureAwait(false);
        var baseReceiptBytes = await NativeCoverageRf3BoundedFileReader.ReadAsync(baseReceiptPath,
            bounds.MaximumManifestBytes, options, token).ConfigureAwait(false);
        var materializer = NativeCoverageRf3FixtureArtifactValidation.ReadMaterializer(materializerBytes, evidence,
            materializerPath);
        var contextPath = Path.Combine(materializer.Directory,
            NativeCoverageRf3FixtureProtocol.ContextManifestHashFilePath);
        var contextBytes = await NativeCoverageRf3BoundedFileReader.ReadAsync(contextPath,
            bounds.MaximumManifestBytes, options, token).ConfigureAwait(false);
        var contextHash = NativeCoverageRf3FixtureArtifactValidation.Hash(contextBytes);
        var toolPackage = NativeCoverageToolPackage.Read();
        var context = NativeCoverageRf3FixtureContextArtifactReader.Read(contextBytes, contextHash, sourceHash,
            bounds, toolPackage);
        var baseReceipt = NativeCoverageRf3FixtureArtifactValidation.ReadBaseReceipt(baseReceiptBytes, sourceRevision,
            context.BaseImageReference);
        NativeCoverageRf3FixtureArtifactValidation.ValidatePreparation(materializer,
            context, baseReceipt, baseReceiptBytes, contextHash, evidence, materializerPath);
        var invocationId = NativeCoverageRf3FixtureInvocationValidation.Validate(invocationBytes,
            materializer.Directory, runId,
            imageReference, sourceHash, sourceManifestPath, baseReceiptPath, baseReceiptBytes, context, bounds,
            toolPackage);
        if (invocationId != context.InvocationId)
        {
            throw NativeCoverageRf3FixtureArtifactValidation.Invalid();
        }
        var imageId = NativeCoverageRf3FixtureArtifactValidation.ReadImageId(inspectBytes);
        return new(materializer.Directory, contextHash, context.DockerfileSha256, imageId, context.Server,
            context.Collector, baseReceipt.ImageReference, NativeCoverageRf3FixtureArtifactValidation.Hash(baseReceiptBytes));
    }
}
