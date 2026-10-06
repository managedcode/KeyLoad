using System.Runtime.ExceptionServices;
using Cartograph.Catalog;

namespace KeyLoad.Artifacts;

/// <summary>Validates and streams a native archive into operation-owned staging.</summary>
internal static class BackupArtifactExtraction
{
    internal static BackupArtifactStaging StageArtifact(string artifactPath,
        BackupArtifactDestinationState destinationState)
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
                    if (!artifact.Entries.Select(entry => entry.RelativePath).Order().SequenceEqual(BackupArtifact.CanonicalFileNames))
                    {
                        throw Errors.Fail(ErrorCode.Validation, BackupArtifact.InvalidCatalog);
                    }
                    staging = BackupArtifactStaging.Create(destinationState);
                    foreach (var entry in artifact.Entries)
                    {
                        CopyEntry(artifact, entry, staging);
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
