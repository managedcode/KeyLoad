namespace KeyLoad.Artifacts;

/// <summary>Claims and publishes a fully validated sibling extraction stage.</summary>
internal sealed class BackupArtifactPublication
{
    private const string StagePrefix = ".keyload-unpack-";
    private const string EmptySuffix = ".empty";
    private const string ClaimSuffix = ".claim";

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
        BackupArtifactPublication? publication = null;
        Exception? failure = null;
        var created = BackupArtifactFailurePolicy.TryCapture(() =>
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
            publication = new BackupArtifactPublication(state, paths);
        }, operationFailure => failure = BackupArtifactFailurePolicy.Combine(failure, operationFailure));
        if (!created)
        {
            var cleanupFailures = CleanupFailedClaim(paths, stageCreated);
            foreach (var cleanupFailure in cleanupFailures)
            {
                failure = BackupArtifactFailurePolicy.Combine(failure, cleanupFailure);
            }
            BackupArtifactFailurePolicy.Throw(failure!);
        }
        return publication!;
    }

    private static List<Exception> CleanupFailedClaim(BackupArtifactStagePaths paths, bool stageCreated)
    {
        var failures = new List<Exception>();
        if (stageCreated)
        {
            BackupArtifactFailurePolicy.TryCapture(() =>
            {
                BackupArtifactStageFileSystem.ValidateRegularDirectory(paths.Stage,
                    BackupArtifactStageFileSystem.InvalidStageDirectoryError);
                Directory.Delete(paths.Stage, recursive: false);
            }, failures.Add);
        }
        BackupArtifactFailurePolicy.TryCapture(() => BackupArtifactStageFileSystem.DeleteClaim(paths.StageClaim), failures.Add);
        return failures;
    }

    internal void Publish()
    {
        BackupArtifactEmptyDestination? empty = null;
        Exception? failure = null;
        var published = BackupArtifactFailurePolicy.TryCapture(() =>
        {
            BackupArtifactStageFileSystem.ValidateRegularDirectory(paths.Stage,
                BackupArtifactStageFileSystem.InvalidStageDirectoryError);
            EnsureDestinationUnchanged();
            empty = destinationState.IsEmpty
                ? new BackupArtifactEmptyDestination(destinationState, paths.EmptyRollback)
                : null;
            empty?.Prepare();
            RemoveStageClaim();
            empty?.ReleaseClaim();
            Directory.Move(paths.Stage, destinationState.Path);
            ownsStage = false;
        }, operationFailure => failure = BackupArtifactFailurePolicy.Combine(failure, operationFailure));
        if (published)
        {
            return;
        }
        var failures = new List<Exception> { failure! };
        if (empty is not null)
        {
            BackupArtifactFailurePolicy.TryCapture(() => empty.Restore(failures), failures.Add);
            BackupArtifactFailurePolicy.TryCapture(() => empty.Cleanup(failures), failures.Add);
        }
        throw new AggregateException(failures);
    }

    internal void Cleanup(List<Exception> failures)
    {
        var stageSettled = true;
        if (ownsStage)
        {
            stageSettled = BackupArtifactFailurePolicy.TryCapture(() =>
            {
                if (BackupArtifactStageFileSystem.PathExists(paths.Stage))
                {
                    BackupArtifactStageFileSystem.ValidateRegularDirectory(paths.Stage,
                        BackupArtifactStageFileSystem.InvalidStageDirectoryError);
                    Directory.Delete(paths.Stage, recursive: false);
                }
                ownsStage = false;
            }, failures.Add);
        }
        if (ownsStageClaim && stageSettled)
        {
            BackupArtifactFailurePolicy.TryCapture(() =>
            {
                BackupArtifactStageFileSystem.DeleteClaim(paths.StageClaim);
                ownsStageClaim = false;
            }, failures.Add);
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
