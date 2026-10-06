namespace KeyLoad.Artifacts;

/// <summary>Claims and publishes a fully validated sibling extraction stage.</summary>
internal sealed class BackupArtifactPublication
{
    private const string StagePrefix = ".keyload-unpack-";
    private const string EmptySuffix = ".empty";
    private const string ClaimSuffix = ".claim";
    private const int EmptyFailureCount = 0;
    private const int PrimaryFailureIndex = 0;

    private readonly BackupArtifactDestinationState destinationState;
    private readonly BackupArtifactStagePaths paths;
    private bool ownsStage = true;
    private bool ownsStageClaim = true;

    private BackupArtifactPublication(BackupArtifactDestinationState destination, BackupArtifactStagePaths paths)
    {
        destinationState = destination;
        this.paths = paths;
    }

    internal string StagePath => paths.Stage;

    internal static BackupArtifactPublication Create(BackupArtifactDestinationState state)
    {
        var parent = Path.GetDirectoryName(state.Path);
        if (string.IsNullOrEmpty(parent))
        {
            throw new IOException(BackupArtifactStageFileSystem.MissingSiblingLocationError);
        }
        Directory.CreateDirectory(parent);
        var wasEmpty = BackupArtifactStageFileSystem.InspectDestination(state.Path,
            BackupArtifactStageFileSystem.NonemptyDestinationError, out var mode);
        if (wasEmpty != state.IsEmpty || mode != state.Mode)
        {
            throw Errors.Fail(ErrorCode.Conflict, BackupArtifactStageFileSystem.DestinationChangedError);
        }
        var stage = Path.Combine(parent, StagePrefix + Guid.NewGuid().ToString(BackupArtifactStageFileSystem.GuidHexFormat));
        var claim = stage + ClaimSuffix;
        var paths = new BackupArtifactStagePaths(stage, claim, stage + EmptySuffix);
        BackupArtifactStageFileSystem.CreateClaim(claim);
        return CreateClaimedStage(state, paths);
    }

    private static BackupArtifactPublication CreateClaimedStage(BackupArtifactDestinationState state,
        BackupArtifactStagePaths paths)
    {
        var stageCreated = false;
        try
        {
            if (BackupArtifactStageFileSystem.PathExists(paths.Stage))
            {
                throw new IOException(BackupArtifactStageFileSystem.OccupiedStageError);
            }
            BackupArtifactStageFileSystem.CreatePrivateDirectory(paths.Stage);
            stageCreated = true;
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(paths.Stage, BackupArtifactStageFileSystem.PrivateDirectoryMode);
            }
            return new BackupArtifactPublication(state, paths);
        }
        catch (Exception failure)
        {
            var cleanupFailures = CleanupFailedClaim(paths, stageCreated);
            if (cleanupFailures.Count > EmptyFailureCount)
            {
                cleanupFailures.Insert(PrimaryFailureIndex, failure);
                throw new AggregateException(cleanupFailures);
            }
            throw;
        }
    }

    private static List<Exception> CleanupFailedClaim(BackupArtifactStagePaths paths, bool stageCreated)
    {
        var failures = new List<Exception>();
        if (stageCreated)
        {
            try
            {
                BackupArtifactStageFileSystem.ValidateRegularDirectory(paths.Stage,
                    BackupArtifactStageFileSystem.InvalidStageDirectoryError);
                Directory.Delete(paths.Stage, recursive: false);
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
        }
        try
        {
            BackupArtifactStageFileSystem.DeleteClaim(paths.StageClaim);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
        return failures;
    }

    internal void Publish()
    {
        BackupArtifactStageFileSystem.ValidateRegularDirectory(paths.Stage,
            BackupArtifactStageFileSystem.InvalidStageDirectoryError);
        EnsureDestinationUnchanged();
        var empty = destinationState.IsEmpty
            ? new BackupArtifactEmptyDestination(destinationState, paths.EmptyRollback)
            : null;
        try
        {
            empty?.Prepare();
            RemoveStageClaim();
            empty?.ReleaseClaim();
            Directory.Move(paths.Stage, destinationState.Path);
            ownsStage = false;
        }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            try
            {
                empty?.Restore(failures);
            }
            catch (Exception rollbackFailure)
            {
                failures.Add(rollbackFailure);
            }
            try
            {
                empty?.Cleanup(failures);
            }
            catch (Exception cleanupFailure)
            {
                failures.Add(cleanupFailure);
            }
            throw new AggregateException(failures);
        }
    }

    internal void Cleanup(List<Exception> failures)
    {
        var stageSettled = true;
        if (ownsStage)
        {
            try
            {
                if (BackupArtifactStageFileSystem.PathExists(paths.Stage))
                {
                    BackupArtifactStageFileSystem.ValidateRegularDirectory(paths.Stage,
                        BackupArtifactStageFileSystem.InvalidStageDirectoryError);
                    Directory.Delete(paths.Stage, recursive: false);
                }
                ownsStage = false;
            }
            catch (Exception failure)
            {
                failures.Add(failure);
                stageSettled = false;
            }
        }
        if (ownsStageClaim && stageSettled)
        {
            try
            {
                BackupArtifactStageFileSystem.DeleteClaim(paths.StageClaim);
                ownsStageClaim = false;
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
        }
    }

    private void EnsureDestinationUnchanged()
    {
        var wasEmpty = BackupArtifactStageFileSystem.InspectDestination(destinationState.Path,
            BackupArtifactStageFileSystem.NonemptyDestinationError, out var currentMode);
        if (wasEmpty != destinationState.IsEmpty || currentMode != destinationState.Mode)
        {
            throw Errors.Fail(ErrorCode.Conflict, BackupArtifactStageFileSystem.DestinationChangedError);
        }
    }

    private void RemoveStageClaim()
    {
        if (ownsStageClaim)
        {
            BackupArtifactStageFileSystem.DeleteClaim(paths.StageClaim);
            ownsStageClaim = false;
        }
    }
}
