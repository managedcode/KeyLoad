using System.Collections.Generic;

namespace KeyLoad.Artifacts;

/// <summary>Owns exact payload files written during one archive extraction.</summary>
internal sealed class BackupArtifactStaging
{
    private const int CanonicalPayloadCount = 3;

    private readonly BackupArtifactPublication publication;
    private readonly List<string> ownedFiles = new(CanonicalPayloadCount);

    private BackupArtifactStaging(BackupArtifactPublication publication) => this.publication = publication;

    internal static BackupArtifactStaging Create(BackupArtifactDestinationState destination)
        => new(BackupArtifactPublication.Create(destination));

    internal FileStream CreateFile(string name)
    {
        BackupArtifactStageFileSystem.ValidateRegularDirectory(publication.StagePath,
            BackupArtifactStageFileSystem.InvalidStageDirectoryError);
        var path = Path.Combine(publication.StagePath, name);
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        RegisterOwnedFile(path, file);
        SetPrivateFileMode(path, file);
        return file;
    }

    private void RegisterOwnedFile(string path, FileStream file)
    {
        try
        {
            ownedFiles.Add(path);
        }
        catch (Exception ownershipFailure)
        {
            var cleanupFailures = new List<Exception> { ownershipFailure };
            try
            {
                file.Dispose();
            }
            catch (Exception disposeFailure)
            {
                cleanupFailures.Add(disposeFailure);
            }
            try
            {
                File.Delete(path);
            }
            catch (Exception deleteFailure)
            {
                cleanupFailures.Add(deleteFailure);
            }
            if (cleanupFailures.Count > 1)
            {
                throw new AggregateException(cleanupFailures);
            }
            throw;
        }
    }

    private static void SetPrivateFileMode(string path, FileStream file)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, BackupArtifactStageFileSystem.PrivateFileMode);
            }
        }
        catch (Exception operationFailure)
        {
            try
            {
                file.Dispose();
            }
            catch (Exception disposeFailure)
            {
                throw new AggregateException(operationFailure, disposeFailure);
            }
            throw;
        }
    }

    internal void Publish()
    {
        publication.Publish();
        ownedFiles.Clear();
    }

    internal void Dispose()
    {
        var failures = new List<Exception>();
        RemoveOwnedFiles(failures);
        publication.Cleanup(failures);
        if (failures.Count > 0)
        {
            throw new AggregateException(failures);
        }
    }

    private void RemoveOwnedFiles(List<Exception> failures)
    {
        if (ownedFiles.Count == 0)
        {
            return;
        }
        try
        {
            BackupArtifactStageFileSystem.ValidateRegularDirectory(publication.StagePath,
                BackupArtifactStageFileSystem.InvalidStageDirectoryError);
        }
        catch (Exception stageFailure)
        {
            failures.Add(stageFailure);
            return;
        }
        foreach (var path in ownedFiles)
        {
            try
            {
                if (BackupArtifactStageFileSystem.TryGetAttributes(path, out var attributes))
                {
                    if ((attributes & FileAttributes.Directory) != BackupArtifactStageFileSystem.NoMatchingAttributes ||
                        (attributes & FileAttributes.ReparsePoint) != BackupArtifactStageFileSystem.NoMatchingAttributes)
                    {
                        throw new IOException(BackupArtifactStageFileSystem.InvalidPayloadPathError);
                    }
                    File.Delete(path);
                }
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
        }
    }
}
