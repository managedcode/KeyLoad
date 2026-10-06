using System.Runtime.ExceptionServices;
using Cartograph.Catalog;

namespace KeyLoad.Artifacts;

/// <summary>Validates and streams a native archive into operation-owned staging.</summary>
internal static class BackupArtifactExtraction
{
    internal static BackupArtifactStaging StageArtifact(string artifactPath,
        BackupArtifactDestinationState destinationState)
    {
        var artifact = CatalogedArtifact.Open(artifactPath);
        BackupArtifactStaging? staging = null;
        Exception? failure = null;
        try
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
        }
        catch (Exception operationFailure)
        {
            failure = operationFailure;
        }
        try
        {
            artifact.Dispose();
        }
        catch (Exception disposeFailure)
        {
            failure = failure is null ? disposeFailure : new AggregateException(failure, disposeFailure);
        }
        if (failure is not null)
        {
            try
            {
                staging?.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                failure = new AggregateException(failure, cleanupFailure);
            }
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        return staging!;
    }

    private static void CopyEntry(CatalogedArtifact artifact, CatalogEntry entry, BackupArtifactStaging staging)
    {
        var file = staging.CreateFile(entry.RelativePath);
        Exception? operationFailure = null;
        try
        {
            if (artifact.CopyTo(entry, file) != entry.Length)
            {
                throw Errors.Fail(ErrorCode.Corruption, BackupArtifact.InvalidLength);
            }
            file.Flush(true);
        }
        catch (Exception failure)
        {
            operationFailure = failure;
        }
        try
        {
            file.Dispose();
        }
        catch (Exception disposeFailure)
        {
            operationFailure = operationFailure is null
                ? disposeFailure
                : new AggregateException(operationFailure, disposeFailure);
        }
        if (operationFailure is not null)
        {
            ExceptionDispatchInfo.Capture(operationFailure).Throw();
        }
    }
}
