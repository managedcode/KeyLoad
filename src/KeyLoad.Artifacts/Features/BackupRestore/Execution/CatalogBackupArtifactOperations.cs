using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.Artifacts;

/// <summary>Owns additive catalog archive verification while reusing the original writer/extraction lifecycle.</summary>
internal static class CatalogBackupArtifactOperations
{
    private const string CatalogMetadataFileName = "catalog-backup.native";
    private const string ManifestFileName = "backup.json";
    private const string JournalFileName = "commands.wal";
    private const string IdentityFileName = "identity.json";
    private const string DigestMismatch = "The catalog archive differs from the independently retained original capture receipt.";
    private static readonly ImmutableArray<string> RequiredNames =
        [ManifestFileName, CatalogMetadataFileName, JournalFileName, IdentityFileName];

    internal static void Pack(string source, string artifactPath, int pieceBytes, string expectedDigest,
        IOptions<ZoneTreeStorageExecutionOptions> options, TimeProvider clock, CancellationToken cancellationToken)
    {
        Require(source, expectedDigest, options, cancellationToken);
        var owned = false;
        try
        {
            BackupArtifact.PackFiles(source, artifactPath, pieceBytes, clock, RequiredNames, out owned);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception primary)
        {
            if (owned)
            {
                try
                { File.Delete(artifactPath); }
                catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            }
            throw;
        }
    }

    internal static void Unpack(string artifactPath, string destination, string expectedDigest,
        IOptions<ZoneTreeStorageExecutionOptions> options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = BackupArtifactStageFileSystem.CaptureDestination(destination,
            BackupArtifactStageFileSystem.NonemptyDestinationError);
        BackupArtifactStaging? staging = null;
        Exception? failure = null;
        try
        {
            BackupArtifactFailurePolicy.TryCapture(() =>
            {
                staging = BackupArtifactExtraction.StageArtifact(artifactPath, target, RequiredNames, cancellationToken);
                Require(staging.DirectoryPath, expectedDigest, options, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                staging.Publish();
            }, error => failure = BackupArtifactFailurePolicy.Combine(failure, error));
        }
        finally
        {
            if (staging is not null)
            {
                BackupArtifactFailurePolicy.TryCapture(staging.Dispose,
                    cleanup => failure = BackupArtifactFailurePolicy.Combine(failure, cleanup));
            }
        }
        if (failure is not null)
        { ExceptionDispatchInfo.Capture(failure).Throw(); }
    }

    private static void Require(string source, string expectedDigest,
        IOptions<ZoneTreeStorageExecutionOptions> options, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(expectedDigest);
        var actual = ZoneTreeStore.ReadVerifiedCatalogBackup(source, options, cancellationToken);
        if (!string.Equals(expectedDigest, actual.ManifestDigest, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.Corruption, DigestMismatch); }
        cancellationToken.ThrowIfCancellationRequested();
    }
}
