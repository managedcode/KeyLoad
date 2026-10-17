using System.Runtime.ExceptionServices;

namespace KeyLoad.Artifacts;

/// <summary>Owns exact payload files written during one archive extraction.</summary>
internal sealed class BackupArtifactStaging
{
    private const int CanonicalPayloadCount = 3;
    private const int NoOwnedFilesCount = 0;
    private const int NoFailuresCount = 0;

    private readonly BackupArtifactPublication publication;
    private readonly List<string> ownedFiles = new(CanonicalPayloadCount);
    private bool published;

    private BackupArtifactStaging(BackupArtifactPublication publication) => this.publication = publication;

    internal static BackupArtifactStaging Create(BackupArtifactDestinationState destination)
        => new(BackupArtifactPublication.Create(destination));

    internal FileStream CreateFile(string name)
    {
        BackupArtifactStageFileSystem.ValidateRegularDirectory(publication.StagePath,
            BackupArtifactStageFileSystem.InvalidStageDirectoryError);
        var path = Path.Combine(publication.StagePath, name);
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var ownershipRecorded = false;
        var returned = false;
        Exception? failure = null;
        BackupArtifactFailurePolicy.TryCapture(() =>
        {
            try
            {
                BackupArtifactFailurePolicy.TryCapture(() =>
                {
                    RegisterOwnedFile(path);
                    ownershipRecorded = true;
                    SetPrivateFileMode(path);
                    returned = true;
                }, operationFailure => failure = BackupArtifactFailurePolicy.Combine(failure, operationFailure));
            }
            finally
            {
                if (!returned)
                {
                    file.Dispose();
                }
            }
        }, disposeFailure => failure = BackupArtifactFailurePolicy.Combine(failure, disposeFailure));
        if (!ownershipRecorded)
        {
            BackupArtifactFailurePolicy.TryCapture(() => File.Delete(path),
                deleteFailure => failure = BackupArtifactFailurePolicy.Combine(failure, deleteFailure));
        }
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        return file;
    }

    private void RegisterOwnedFile(string path) => ownedFiles.Add(path);

    private static void SetPrivateFileMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, BackupArtifactStageFileSystem.PrivateFileMode);
        }
    }

    internal string DirectoryPath => publication.StagePath;

    internal void Publish()
    {
        publication.Publish();
        ownedFiles.Clear();
        published = true;
    }

    internal void Dispose()
    {
        if (published)
        {
            return;
        }
        var failures = new List<Exception>();
        RemoveOwnedFiles(failures);
        BackupArtifactFailurePolicy.TryCapture(() => publication.Cleanup(failures), failures.Add);
        if (failures.Count > NoFailuresCount)
        {
            throw new AggregateException(failures);
        }
    }

    private void RemoveOwnedFiles(List<Exception> failures)
    {
        if (ownedFiles.Count == NoOwnedFilesCount)
        {
            return;
        }
        var stageValidated = BackupArtifactFailurePolicy.TryCapture(() =>
            BackupArtifactStageFileSystem.ValidateRegularDirectory(publication.StagePath,
                BackupArtifactStageFileSystem.InvalidStageDirectoryError), failures.Add);
        if (!stageValidated)
        {
            return;
        }
        foreach (var path in ownedFiles)
        {
            RemoveOwnedFile(path, failures);
        }
    }

    private static void RemoveOwnedFile(string path, List<Exception> failures)
    {
        BackupArtifactFailurePolicy.TryCapture(() =>
        {
            if (!BackupArtifactStageFileSystem.TryGetAttributes(path, out var attributes))
            {
                return;
            }
            if ((attributes & FileAttributes.Directory) != BackupArtifactStageFileSystem.NoMatchingAttributes ||
                (attributes & FileAttributes.ReparsePoint) != BackupArtifactStageFileSystem.NoMatchingAttributes)
            {
                throw new IOException(BackupArtifactStageFileSystem.InvalidPayloadPathError);
            }
            File.Delete(path);
        }, failures.Add);
    }
}
