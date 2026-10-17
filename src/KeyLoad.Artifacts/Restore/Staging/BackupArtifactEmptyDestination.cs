namespace KeyLoad.Artifacts;

/// <summary>Temporarily preserves an initially empty caller destination during publication.</summary>
internal sealed class BackupArtifactEmptyDestination
{
    private const string RetainedPrefix = ".keyload-unpack-empty-";
    private const string ClaimSuffix = ".claim";

    private readonly BackupArtifactDestinationState destinationState;
    private readonly string rollback;
    private readonly string claim;
    private bool ownsClaim;
    private bool movedOut;
    private bool removed;
    private bool keepClaim;

    internal BackupArtifactEmptyDestination(BackupArtifactDestinationState destination, string rollbackPath)
    {
        destinationState = destination;
        rollback = rollbackPath;
        claim = rollbackPath + ClaimSuffix;
    }

    internal void Prepare()
    {
        var wasEmpty = BackupArtifactStageFileSystem.InspectDestination(destinationState.Path,
            BackupArtifactStageFileSystem.NonemptyDestinationError, out var currentMode);
        if (!wasEmpty || currentMode != destinationState.Mode)
        {
            throw Errors.Fail(ErrorCode.Conflict, BackupArtifactStageFileSystem.DestinationChangedError);
        }
        BackupArtifactStageFileSystem.CreateClaim(claim);
        ownsClaim = true;
        if (BackupArtifactStageFileSystem.PathExists(rollback))
        {
            throw new IOException(BackupArtifactStageFileSystem.OccupiedRollbackError);
        }
        Directory.Move(destinationState.Path, rollback);
        movedOut = true;
        Directory.Delete(rollback, recursive: false);
        removed = true;
    }
    internal void ReleaseClaim()
    {
        if (ownsClaim)
        {
            BackupArtifactStageFileSystem.DeleteClaim(claim);
            ownsClaim = false;
        }
    }
    internal void Restore(List<Exception> failures)
    {
        if (!movedOut)
        {
            return;
        }
        var publicPathExists = false;
        if (!BackupArtifactFailurePolicy.TryCapture(() =>
            publicPathExists = BackupArtifactStageFileSystem.PathExists(destinationState.Path), failures.Add))
        {
            keepClaim = ownsClaim;
            return;
        }
        if (publicPathExists)
        {
            PreserveRollback(failures, publicPathOccupied: true);
            failures.Add(new IOException(BackupArtifactStageFileSystem.RacingDestinationError));
            return;
        }
        if (!removed)
        {
            var rollbackExists = false;
            if (!BackupArtifactFailurePolicy.TryCapture(() =>
                rollbackExists = BackupArtifactStageFileSystem.PathExists(rollback), failures.Add))
            {
                keepClaim = ownsClaim;
                return;
            }
            if (rollbackExists)
            {
                if (!BackupArtifactFailurePolicy.TryCapture(
                    () => Directory.Move(rollback, destinationState.Path), failures.Add))
                {
                    AddRacingDestinationIfPresent(failures);
                    keepClaim = ownsClaim;
                    return;
                }
                movedOut = false;
                BackupArtifactFailurePolicy.TryCapture(ReleaseClaim, failures.Add);
                return;
            }
        }
        PreserveRollback(failures);
    }
    internal void Cleanup(List<Exception> failures)
    {
        if (!ownsClaim || keepClaim)
        {
            return;
        }
        BackupArtifactFailurePolicy.TryCapture(() =>
        {
            BackupArtifactStageFileSystem.DeleteClaim(claim);
            ownsClaim = false;
        }, failures.Add);
    }
    private void PreserveRollback(List<Exception> failures, bool publicPathOccupied = false)
    {
        if (!removed)
        {
            var rollbackExists = false;
            if (!BackupArtifactFailurePolicy.TryCapture(() =>
                rollbackExists = BackupArtifactStageFileSystem.PathExists(rollback), failures.Add))
            {
                keepClaim = ownsClaim;
                return;
            }
            if (rollbackExists)
            {
                keepClaim = ownsClaim;
                return;
            }
        }
        RestoreNewOwnedRollback(failures, publicPathOccupied);
    }
    private void RestoreNewOwnedRollback(List<Exception> failures, bool publicPathOccupied)
    {
        var parent = Path.GetDirectoryName(destinationState.Path)!;
        var retained = Path.Combine(parent,
            RetainedPrefix + Guid.NewGuid().ToString(BackupArtifactStageFileSystem.GuidHexFormat));
        var retainedClaim = retained + ClaimSuffix;
        if (!BackupArtifactFailurePolicy.TryCapture(() => BackupArtifactStageFileSystem.CreateClaim(retainedClaim), failures.Add))
        {
            return;
        }
        var retainedExists = false;
        if (!BackupArtifactFailurePolicy.TryCapture(() =>
            retainedExists = BackupArtifactStageFileSystem.PathExists(retained), failures.Add))
        {
            DeleteRetainedClaim(retainedClaim, failures);
            return;
        }
        if (retainedExists)
        {
            failures.Add(new IOException(BackupArtifactStageFileSystem.OccupiedRetainedRollbackError));
            DeleteRetainedClaim(retainedClaim, failures);
            return;
        }
        if (!BackupArtifactFailurePolicy.TryCapture(
            () => BackupArtifactStageFileSystem.CreateEmptyDirectory(retained, destinationState.Mode), failures.Add))
        {
            DeleteRetainedClaim(retainedClaim, failures);
            return;
        }
        if (!BackupArtifactFailurePolicy.TryCapture(() => RestoreUnixMode(retained), failures.Add))
        {
            keepClaim = ownsClaim;
            return;
        }
        if (!BackupArtifactFailurePolicy.TryCapture(() => Directory.Move(retained, destinationState.Path), failures.Add))
        {
            AddRacingDestinationIfPresent(failures, publicPathOccupied);
            keepClaim = ownsClaim;
            return;
        }
        movedOut = false;
        if (!BackupArtifactFailurePolicy.TryCapture(() => BackupArtifactStageFileSystem.DeleteClaim(retainedClaim), failures.Add))
        {
            DeleteRetainedClaim(retainedClaim, failures);
            return;
        }
    }
    private void AddRacingDestinationIfPresent(List<Exception> failures, bool publicPathOccupied = false,
        bool retainedDirectoryStillOwned = true)
    {
        if (publicPathOccupied || !retainedDirectoryStillOwned)
        {
            return;
        }
        var publicPathExists = false;
        if (BackupArtifactFailurePolicy.TryCapture(() =>
            publicPathExists = BackupArtifactStageFileSystem.PathExists(destinationState.Path), failures.Add) && publicPathExists)
        {
            failures.Add(new IOException(BackupArtifactStageFileSystem.RacingDestinationError));
        }
    }
    private void RestoreUnixMode(string path)
    {
        if (!OperatingSystem.IsWindows() && destinationState.Mode is not null)
        {
            File.SetUnixFileMode(path, destinationState.Mode.Value);
        }
    }
    private static void DeleteRetainedClaim(string path, List<Exception> failures)
        => BackupArtifactFailurePolicy.TryCapture(() => BackupArtifactStageFileSystem.DeleteClaim(path), failures.Add);
}
