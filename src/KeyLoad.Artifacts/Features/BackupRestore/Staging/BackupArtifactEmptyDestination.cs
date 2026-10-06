using System.Collections.Generic;

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
        if (BackupArtifactStageFileSystem.PathExists(destinationState.Path))
        {
            PreserveRollback(failures, publicPathOccupied: true);
            failures.Add(new IOException(BackupArtifactStageFileSystem.RacingDestinationError));
            return;
        }
        if (!removed && BackupArtifactStageFileSystem.PathExists(rollback))
        {
            try
            {
                Directory.Move(rollback, destinationState.Path);
                movedOut = false;
            }
            catch (Exception restoreFailure)
            {
                failures.Add(restoreFailure);
                if (BackupArtifactStageFileSystem.PathExists(destinationState.Path))
                {
                    failures.Add(new IOException(BackupArtifactStageFileSystem.RacingDestinationError));
                }
                keepClaim = ownsClaim;
                return;
            }
            try
            {
                ReleaseClaim();
            }
            catch (Exception cleanupFailure)
            {
                failures.Add(cleanupFailure);
            }
            return;
        }
        PreserveRollback(failures);
    }

    internal void Cleanup(List<Exception> failures)
    {
        if (!ownsClaim || keepClaim)
        {
            return;
        }
        try
        {
            BackupArtifactStageFileSystem.DeleteClaim(claim);
            ownsClaim = false;
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
    }

    private void PreserveRollback(List<Exception> failures, bool publicPathOccupied = false)
    {
        if (!removed && BackupArtifactStageFileSystem.PathExists(rollback))
        {
            keepClaim = ownsClaim;
            return;
        }
        RestoreNewOwnedRollback(failures, publicPathOccupied);
    }

    private void RestoreNewOwnedRollback(List<Exception> failures, bool publicPathOccupied)
    {
        var parent = Path.GetDirectoryName(destinationState.Path)!;
        var retained = Path.Combine(parent,
            RetainedPrefix + Guid.NewGuid().ToString(BackupArtifactStageFileSystem.GuidHexFormat));
        var retainedClaim = retained + ClaimSuffix;
        var ownsRetainedClaim = false;
        var retainedCreated = false;
        var moveAttempted = false;
        try
        {
            BackupArtifactStageFileSystem.CreateClaim(retainedClaim);
            ownsRetainedClaim = true;
            if (BackupArtifactStageFileSystem.PathExists(retained))
            {
                throw new IOException(BackupArtifactStageFileSystem.OccupiedRetainedRollbackError);
            }
            BackupArtifactStageFileSystem.CreateEmptyDirectory(retained, destinationState.Mode);
            retainedCreated = true;
            RestoreUnixMode(retained);
            moveAttempted = true;
            Directory.Move(retained, destinationState.Path);
            retainedCreated = false;
            BackupArtifactStageFileSystem.DeleteClaim(retainedClaim);
            ownsRetainedClaim = false;
        }
        catch (Exception failure)
        {
            failures.Add(failure);
            if (!publicPathOccupied && moveAttempted && retainedCreated &&
                BackupArtifactStageFileSystem.PathExists(destinationState.Path))
            {
                failures.Add(new IOException(BackupArtifactStageFileSystem.RacingDestinationError));
            }
            if (retainedCreated)
            {
                keepClaim = true;
            }
            else if (ownsRetainedClaim)
            {
                DeleteRetainedClaim(retainedClaim, failures);
            }
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
    {
        try
        {
            BackupArtifactStageFileSystem.DeleteClaim(path);
        }
        catch (Exception cleanupFailure)
        {
            failures.Add(cleanupFailure);
        }
    }

}
