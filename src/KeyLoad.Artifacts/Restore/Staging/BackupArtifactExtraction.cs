using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Cartograph.Catalog;

namespace KeyLoad.Artifacts;

/// <summary>Validates and streams a native archive into operation-owned staging.</summary>
internal static class BackupArtifactExtraction
{
    internal static BackupArtifactStaging StageArtifact(string artifactPath,
        BackupArtifactDestinationState destinationState) =>
        StageArtifact(artifactPath, destinationState, BackupArtifact.CanonicalFileNames, CancellationToken.None);

    internal static BackupArtifactStaging StageArtifact(string artifactPath, BackupArtifactDestinationState destinationState,
        ImmutableArray<string> requiredNames, CancellationToken cancellationToken)
    {
        BackupArtifactStaging? staging = null;
        Exception? failure = null;
        BackupArtifactFailurePolicy.TryCapture(() =>
        {
            var artifact = CatalogedArtifact.Open(artifactPath);
            try
            {
                BackupArtifactFailurePolicy.TryCapture(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!artifact.Entries.Select(entry => entry.RelativePath).Order().SequenceEqual(requiredNames))
                    {
                        throw Errors.Fail(ErrorCode.Validation, BackupArtifact.InvalidCatalog);
                    }
                    staging = BackupArtifactStaging.Create(destinationState);
                    foreach (var entry in artifact.Entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        CopyEntry(artifact, entry, staging);
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }, operationFailure => failure = BackupArtifactFailurePolicy.Combine(failure, operationFailure));
            }
            finally
            {
                artifact.Dispose();
            }
        }, disposeFailure => failure = BackupArtifactFailurePolicy.Combine(failure, disposeFailure));
        if (failure is not null)
        {
            if (staging is not null)
            {
                BackupArtifactFailurePolicy.TryCapture(staging.Dispose,
                    cleanupFailure => failure = BackupArtifactFailurePolicy.Combine(failure, cleanupFailure));
            }
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        return staging!;
    }

    private static void CopyEntry(CatalogedArtifact artifact, CatalogEntry entry, BackupArtifactStaging staging)
    {
        Exception? failure = null;
        BackupArtifactFailurePolicy.TryCapture(() =>
        {
            var file = staging.CreateFile(entry.RelativePath);
            try
            {
                BackupArtifactFailurePolicy.TryCapture(() =>
                {
                    if (artifact.CopyTo(entry, file) != entry.Length)
                    {
                        throw Errors.Fail(ErrorCode.Corruption, BackupArtifact.InvalidLength);
                    }
                    file.Flush(true);
                }, operationFailure => failure = BackupArtifactFailurePolicy.Combine(failure, operationFailure));
            }
            finally
            {
                file.Dispose();
            }
        }, disposeFailure => failure = BackupArtifactFailurePolicy.Combine(failure, disposeFailure));
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
