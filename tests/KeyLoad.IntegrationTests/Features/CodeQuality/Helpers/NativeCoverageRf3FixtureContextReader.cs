using KeyLoad.AppHost.Features.CodeQuality;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

internal static class NativeCoverageRf3FixtureContextReader
{
    internal static async Task<NativeCoverageRf3FixtureContext?> ReadAsync(string fixtureId,
        IOptions<NativeCoverageExecutionOptions> options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var execution = options.Value;
        if (!execution.IsValid())
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        }
        var selected = ReadSelection();
        if (selected is null)
        {
            return null;
        }
        var selection = selected.Value;
        var sourceRevision = NativeCoverageRf3FixtureOptions.RequiredEnvironment(
            NativeCoverageRf3FixtureOptions.GithubShaEnvironment);
        var source = await NativeCoverageRf3FixtureSourceReader.ReadSourceAsync(selection.SourcePath, sourceRevision,
            options, cancellationToken).ConfigureAwait(false);
        var run = await NativeCoverageRf3FixtureSourceReader.ReadRunAsync(selection.RunPath, selection.RunId, source.Hash,
            source.Revision, options, cancellationToken).ConfigureAwait(false);
        var artifacts = await NativeCoverageRf3FixtureArtifactReader.ReadAsync(selection.EvidenceRoot, selection.RunId,
            selection.SourcePath, selection.ImageReference, source.Hash, source.Revision, run.Bounds,
            options, cancellationToken).ConfigureAwait(false);
        var fixtureRoot = Path.Combine(selection.EvidenceRoot, NativeCoverageRf3FixtureProtocol.FixtureRootPrefix + fixtureId);
        return new(selection.RunId, Path.GetFullPath(selection.RunPath), Path.GetFullPath(selection.SourcePath),
            source.Hash, source.Revision, selection.ImageReference, artifacts.ImageId, artifacts.ContextDirectory,
            artifacts.ContextHash, artifacts.DockerfileHash, selection.EvidenceRoot, fixtureId, fixtureRoot,
            run.Cases, artifacts.Server, artifacts.Collector, run.Bounds, artifacts.BaseImageReference, artifacts.BaseReceiptHash);
    }

    private static (string SourcePath, string RunPath, string RunId, string ImageReference, string EvidenceRoot)? ReadSelection()
    {
        var mode = NativeCoverageRf3FixtureOptions.OptionalEnvironment(NativeCoverageRf3FixtureProtocol.ModeEnvironment);
        var sourcePathValue = NativeCoverageRf3FixtureOptions.OptionalEnvironment(
            NativeCoverageRf3FixtureProtocol.SourceManifestEnvironment);
        var selectedRun = NativeCoverageRf3FixtureOptions.OptionalEnvironment(NativeCoverageRf3FixtureProtocol.RunManifestEnvironment);
        var runIdValue = NativeCoverageRf3FixtureOptions.OptionalEnvironment(NativeCoverageRf3FixtureProtocol.RunIdEnvironment);
        var imageValue = NativeCoverageRf3FixtureOptions.OptionalEnvironment(NativeCoverageRf3FixtureProtocol.ImageReferenceEnvironment);
        if (mode is null && sourcePathValue is null && selectedRun is null && runIdValue is null && imageValue is null)
        {
            return null;
        }
        var sourcePath = NativeCoverageRf3FixtureOptions.RequiredEnvironment(
            NativeCoverageRf3FixtureProtocol.SourceManifestEnvironment);
        var runPath = NativeCoverageRf3FixtureOptions.RequiredEnvironment(
            NativeCoverageRf3FixtureProtocol.RunManifestEnvironment);
        var runId = NativeCoverageRf3FixtureOptions.RequiredEnvironment(
            NativeCoverageRf3FixtureProtocol.RunIdEnvironment);
        var imageReference = NativeCoverageRf3FixtureOptions.RequiredEnvironment(
            NativeCoverageRf3FixtureProtocol.ImageReferenceEnvironment);
        if (mode != NativeCoverageRf3FixtureProtocol.Mode
            || !NativeCoverageRf3FixtureSourceReader.IsRunIdentity(runId, imageReference))
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        }
        var evidence = Path.GetDirectoryName(Path.GetFullPath(runPath))
            ?? throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        if (!Path.IsPathFullyQualified(runPath)
            || Path.GetFileName(runPath) != NativeCoverageRf3FixtureProtocol.RunManifestName)
        {
            throw new InvalidOperationException(NativeCoverageRf3FixtureProtocol.InvalidContext);
        }
        return (sourcePath, runPath, runId, imageReference, evidence);
    }
}
