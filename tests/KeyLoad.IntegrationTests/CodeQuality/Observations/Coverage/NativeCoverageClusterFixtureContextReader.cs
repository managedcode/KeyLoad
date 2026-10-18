using KeyLoad.AppHost.Features.CodeQuality;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageClusterFixtureContextReader
{
    internal static async Task<NativeCoverageClusterFixtureContext?> ReadAsync(string fixtureId,
        IOptions<NativeCoverageExecutionOptions> options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var execution = options.Value;
        if (!execution.IsValid())
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
        }
        var selected = ReadSelection();
        if (selected is null)
        {
            return null;
        }
        var selection = selected.Value;
        var sourceRevision = NativeCoverageClusterFixtureOptions.RequiredEnvironment(
            NativeCoverageClusterFixtureOptions.GithubShaEnvironment);
        var source = await NativeCoverageClusterFixtureSourceReader.ReadSourceAsync(selection.SourcePath, sourceRevision,
            options, cancellationToken).ConfigureAwait(false);
        var run = await NativeCoverageClusterFixtureSourceReader.ReadRunAsync(selection.RunPath, selection.RunId, source.Hash,
            source.Revision, options, cancellationToken).ConfigureAwait(false);
        var artifacts = await NativeCoverageClusterFixtureArtifactReader.ReadAsync(selection.EvidenceRoot, selection.RunId,
            selection.SourcePath, selection.ImageReference, source.Hash, source.Revision, run.Bounds,
            options, cancellationToken).ConfigureAwait(false);
        var fixtureRoot = Path.Combine(selection.EvidenceRoot, NativeCoverageClusterFixtureProtocol.FixtureRootPrefix + fixtureId);
        return new(selection.RunId, Path.GetFullPath(selection.RunPath), Path.GetFullPath(selection.SourcePath),
            source.Hash, source.Revision, selection.ImageReference, artifacts.ImageId, artifacts.ContextDirectory,
            artifacts.ContextHash, artifacts.DockerfileHash, selection.EvidenceRoot, fixtureId, fixtureRoot,
            run.Cases, artifacts.Server, artifacts.Collector, run.Bounds, artifacts.BaseImageReference, artifacts.BaseReceiptHash);
    }

    private static (string SourcePath, string RunPath, string RunId, string ImageReference, string EvidenceRoot)? ReadSelection()
    {
        var mode = NativeCoverageClusterFixtureOptions.OptionalEnvironment(NativeCoverageClusterFixtureProtocol.ModeEnvironment);
        var sourcePathValue = NativeCoverageClusterFixtureOptions.OptionalEnvironment(
            NativeCoverageClusterFixtureProtocol.SourceManifestEnvironment);
        var selectedRun = NativeCoverageClusterFixtureOptions.OptionalEnvironment(NativeCoverageClusterFixtureProtocol.RunManifestEnvironment);
        var runIdValue = NativeCoverageClusterFixtureOptions.OptionalEnvironment(NativeCoverageClusterFixtureProtocol.RunIdEnvironment);
        var imageValue = NativeCoverageClusterFixtureOptions.OptionalEnvironment(NativeCoverageClusterFixtureProtocol.ImageReferenceEnvironment);
        if (mode is null && sourcePathValue is null && selectedRun is null && runIdValue is null && imageValue is null)
        {
            return null;
        }
        var sourcePath = NativeCoverageClusterFixtureOptions.RequiredEnvironment(
            NativeCoverageClusterFixtureProtocol.SourceManifestEnvironment);
        var runPath = NativeCoverageClusterFixtureOptions.RequiredEnvironment(
            NativeCoverageClusterFixtureProtocol.RunManifestEnvironment);
        var runId = NativeCoverageClusterFixtureOptions.RequiredEnvironment(
            NativeCoverageClusterFixtureProtocol.RunIdEnvironment);
        var imageReference = NativeCoverageClusterFixtureOptions.RequiredEnvironment(
            NativeCoverageClusterFixtureProtocol.ImageReferenceEnvironment);
        if (mode != NativeCoverageClusterFixtureProtocol.Mode
            || !NativeCoverageClusterFixtureSourceReader.IsRunIdentity(runId, imageReference))
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
        }
        var evidence = Path.GetDirectoryName(Path.GetFullPath(runPath))
            ?? throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
        if (!Path.IsPathFullyQualified(runPath)
            || Path.GetFileName(runPath) != NativeCoverageClusterFixtureProtocol.RunManifestName)
        {
            throw new InvalidOperationException(NativeCoverageClusterFixtureProtocol.InvalidContext);
        }
        return (sourcePath, runPath, runId, imageReference, evidence);
    }
}
