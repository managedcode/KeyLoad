namespace KeyLoad.Artifacts;

/// <summary>Provides bounded filesystem claims and destination inspection for archive staging.</summary>
internal static class BackupArtifactStageFileSystem
{
    internal const FileAttributes NoMatchingAttributes = 0;
    internal const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    internal const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    internal const string GuidHexFormat = "N";
    internal const string ReplacedClaimError = "An owned unpack claim is missing or was replaced.";
    internal const string InvalidStageDirectoryError = "The owned unpack staging directory was replaced.";
    internal const string InvalidPayloadPathError = "An owned unpack payload path was replaced by a non-regular entry.";
    internal const string MissingSiblingLocationError = "The unpack destination has no sibling staging location.";
    internal const string OccupiedStageError = "The claimed unpack staging path is already occupied.";
    internal const string OccupiedRollbackError = "The claimed empty-destination rollback path is already occupied.";
    internal const string OccupiedRetainedRollbackError = "The claimed empty rollback path is already occupied.";
    internal const string DestinationChangedError = "The unpack destination changed during extraction.";
    internal const string NonemptyDestinationError = "Artifact extraction requires an empty destination.";
    internal const string RacingDestinationError = "A racing path prevents restoring the empty unpack destination.";

    internal static bool InspectDestination(string path, string nonemptyMessage, out UnixFileMode? mode)
    {
        mode = null;
        if (!TryGetAttributes(path, out var attributes))
        {
            return false;
        }
        if ((attributes & FileAttributes.Directory) == NoMatchingAttributes ||
            (attributes & FileAttributes.ReparsePoint) != NoMatchingAttributes ||
            Directory.EnumerateFileSystemEntries(path).Any())
        {
            throw Errors.Fail(ErrorCode.Conflict, nonemptyMessage);
        }
        if (!OperatingSystem.IsWindows())
        {
            mode = File.GetUnixFileMode(path);
        }
        return true;
    }

    internal static BackupArtifactDestinationState CaptureDestination(string path, string nonemptyMessage)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var wasEmpty = InspectDestination(fullPath, nonemptyMessage, out var mode);
        return new(fullPath, wasEmpty, mode);
    }

    internal static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            attributes = NoMatchingAttributes;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            attributes = NoMatchingAttributes;
            return false;
        }
    }

    internal static bool PathExists(string path) => TryGetAttributes(path, out _);

    internal static void ValidateRegularDirectory(string path, string errorMessage)
    {
        if (!TryGetAttributes(path, out var attributes) ||
            (attributes & FileAttributes.Directory) == NoMatchingAttributes ||
            (attributes & FileAttributes.ReparsePoint) != NoMatchingAttributes)
        {
            throw new IOException(errorMessage);
        }
    }

    internal static void CreateClaim(string path)
    {
        using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
    }

    internal static void DeleteClaim(string path)
    {
        if (!TryGetAttributes(path, out var attributes) ||
            (attributes & FileAttributes.Directory) != NoMatchingAttributes ||
            (attributes & FileAttributes.ReparsePoint) != NoMatchingAttributes)
        {
            throw new IOException(ReplacedClaimError);
        }
        File.Delete(path);
    }

    internal static void CreatePrivateDirectory(string path) => Directory.CreateDirectory(path);

    internal static void CreateEmptyDirectory(string path, UnixFileMode? mode)
    {
        if (OperatingSystem.IsWindows() || mode is null)
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, mode.Value);
        }
    }
}

internal sealed record BackupArtifactDestinationState(string Path, bool IsEmpty, UnixFileMode? Mode);

internal sealed record BackupArtifactStagePaths(string Stage, string StageClaim, string EmptyRollback);
