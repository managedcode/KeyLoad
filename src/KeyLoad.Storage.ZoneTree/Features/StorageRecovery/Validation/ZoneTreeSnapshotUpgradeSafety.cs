namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeSnapshotUpgradeSafety
{
    private const int MinimumBudgetExclusive = 0;
    private const int NoFileAttributes = 0;

    private const string InvalidOptionsMessage = "Snapshot upgrade budgets must be positive and within current defaults.";
    private const string InvalidPathMessage = "Snapshot upgrade paths must be distinct regular files without links.";

    internal static ZoneTreeSnapshotUpgradeSettings ValidateOptions(ZoneTreeSnapshotUpgradeOptions? options,
        ZoneTreeStorageExecutionOptions policy)
    {
        var settings = options ?? new ZoneTreeSnapshotUpgradeOptions();
        var frameBytes = settings.MaxFrameBytesOverride ?? policy.MaxFrameBytes;
        var snapshotBytes = settings.MaxSnapshotBytesOverride ?? policy.MaxSnapshotBytes;
        if (frameBytes <= MinimumBudgetExclusive || frameBytes > policy.MaxFrameBytes
            || snapshotBytes <= MinimumBudgetExclusive || snapshotBytes > policy.MaxSnapshotBytes)
        { throw Errors.Fail(ErrorCode.Validation, InvalidOptionsMessage); }

        var effective = policy with { MaxFrameBytes = frameBytes, MaxSnapshotBytes = snapshotBytes };
        var descriptor = new ZoneTreeStoreOptions(string.Empty)
        { MaxFrameBytes = frameBytes, MaxSnapshotBytes = snapshotBytes, FaultObserver = settings.FaultObserver }
            .WithExecutionSnapshot(effective);
        return new(frameBytes, snapshotBytes, settings.FaultObserver, descriptor);
    }

    internal static void ValidateIncarnation(Guid incarnation)
    {
        if (incarnation == Guid.Empty)
        { throw Errors.Fail(ErrorCode.Validation, ZoneTreePersistenceFormat.SnapshotScopeInvalid); }
    }

    internal static void ValidateSourceEpoch(int sourceDataEpoch)
    {
        if (sourceDataEpoch is not (ZoneTreePersistenceFormat.Native5DataEpoch or ZoneTreePersistenceFormat.Native6DataEpoch))
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ZoneTreePersistenceFormat.SnapshotFormatUnsupported); }
    }

    internal static void ValidatePaths(string source, string destination)
    {
        if (string.Equals(source, destination, PathComparison()))
        { throw Errors.Fail(ErrorCode.Validation, InvalidPathMessage); }

        VerifySourcePath(source);
        VerifyAbsentDestination(destination);
    }

    internal static void VerifySourcePath(string path)
    {
        var attributes = ZoneTreeFormatUpgradePathSafety.VerifyNoLinks(path, allowMissingFinal: false);
        if (attributes is null || (attributes.Value & FileAttributes.Directory) != NoFileAttributes)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, InvalidPathMessage); }
    }

    internal static void VerifyAbsentDestination(string path)
    {
        var attributes = ZoneTreeFormatUpgradePathSafety.VerifyNoLinks(path, allowMissingFinal: true);
        if (attributes is not null)
        { throw Errors.Fail(ErrorCode.Conflict, ZoneTreePersistenceFormat.RestoreDestinationNotEmpty); }
    }

    internal static string Normalize(string path) => Path.GetFullPath(path);

    private static StringComparison PathComparison()
        => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

internal sealed record ZoneTreeSnapshotUpgradeSettings(int MaxFrameBytes, long MaxSnapshotBytes,
    Action<CommitStage, long, int>? FaultObserver, ZoneTreeStoreOptions Descriptor);
