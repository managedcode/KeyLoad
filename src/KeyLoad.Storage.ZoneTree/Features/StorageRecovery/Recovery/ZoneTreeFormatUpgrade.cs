namespace KeyLoad.Storage.ZoneTree;

    /// <summary>Copies one stopped native5 or native6 store into a separately published native7 store.</summary>
public static class ZoneTreeFormatUpgrade
{
    /// <summary>Validates a private stopped native5 or native6 authority copy without opening or rebuilding its tree.</summary>
    /// <param name="sourceOptions">Private copied source and exact configured authority and finite budgets.</param>
    /// <returns>The actual verified source identity; signing credentials remain private to the caller.</returns>
    public static StoreIdentity VerifySource(ZoneTreeStoreOptions sourceOptions)
    {
        ArgumentNullException.ThrowIfNull(sourceOptions);
        ValidateOptions(sourceOptions);
        using var source = ZoneTreeFormatUpgradeSource.Open(sourceOptions.Directory, sourceOptions);
        source.VerifyUnchanged();
        return source.Identity;
    }

    /// <summary>Validates the existing native receipt for a converted store owned by an exact source copy.</summary>
    /// <param name="source">Private original native5 or native6 authority copy.</param>
    /// <param name="destinationOptions">Matching converted target and configured source authority.</param>
    public static void VerifyOwnedReceipt(string source, ZoneTreeStoreOptions destinationOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(destinationOptions);
        ValidateOptions(destinationOptions);
        var paths = NormalizePaths(source, destinationOptions.Directory);
        using var lease = ZoneTreeFormatUpgradeSource.Open(paths.Source, destinationOptions);
        ZoneTreeFormatUpgradeStage.VerifyPublishable(paths.Destination, CreateReceipt(paths, lease));
        lease.VerifyUnchanged();
    }

    /// <summary>Removes only the verified nested receipt after its owning node has captured complete authority.</summary>
    /// <param name="source">Private original native5 or native6 authority copy retained until node verification.</param>
    /// <param name="destinationOptions">The verified converted target.</param>
    public static void RemoveOwnedReceipt(string source, ZoneTreeStoreOptions destinationOptions)
    {
        VerifyOwnedReceipt(source, destinationOptions);
        File.Delete(Path.Combine(destinationOptions.Directory, ZoneTreeFormatUpgradeStage.ReceiptFileName));
    }

    /// <summary>Performs the closed native5/WAL4/checkpoint3 or native6/WAL4/checkpoint4 to native7/WAL4/checkpoint5 offline copy.</summary>
    /// <param name="source">Existing source store directory, held under its canonical owner lock.</param>
    /// <param name="destinationOptions">Separate target directory, matching source authority and storage budgets.</param>
    /// <returns>The new current-format identity after verified atomic publication.</returns>
    public static StoreIdentity Upgrade(string source, ZoneTreeStoreOptions destinationOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(destinationOptions);
        ValidateOptions(destinationOptions);
        var paths = NormalizePaths(source, destinationOptions.Directory);
        using var sourceLease = ZoneTreeFormatUpgradeSource.Open(paths.Source, destinationOptions);
        var receipt = CreateReceipt(paths, sourceLease);
        destinationOptions.FaultObserver?.Invoke(CommitStage.UpgradeSourceVerified, sourceLease.Position, 0);
        if (ZoneTreeFormatUpgradePathSafety.VerifyNoLinks(paths.Destination, allowMissingFinal: true) is { } destinationAttributes)
        {
            if ((destinationAttributes & FileAttributes.Directory) == 0)
            {
                throw Errors.Fail(ErrorCode.Conflict, ZoneTreePersistenceFormat.RestoreDestinationNotEmpty);
            }
            ZoneTreeFormatUpgradeStage.VerifyDirectory(paths.Destination);
            if (File.Exists(Path.Combine(paths.Destination, ZoneTreeFormatUpgradeStage.ReceiptFileName)))
            {
                return VerifyPublished(paths.Destination, receipt, sourceLease, destinationOptions);
            }
            if (Directory.EnumerateFileSystemEntries(paths.Destination).Any())
            {
                throw Errors.Fail(ErrorCode.Conflict, ZoneTreePersistenceFormat.RestoreDestinationNotEmpty);
            }
        }
        if (ZoneTreeFormatUpgradePathSafety.VerifyNoLinks(paths.Staging, allowMissingFinal: true) is { } stagingAttributes
            && (stagingAttributes & FileAttributes.Directory) == 0)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, UpgradePathAmbiguous);
        }

        ZoneTreeFormatUpgradeStage.CreateOrReset(paths.Staging, receipt);
        ZoneTreeFormatUpgradeStage.CopySources(sourceLease, paths.Staging);
        destinationOptions.FaultObserver?.Invoke(CommitStage.UpgradePrepared, sourceLease.Position, 0);
        var stageOptions = TargetOptions(destinationOptions, paths.Staging, sourceLease.Identity);
        var identity = ZoneTreeFormatUpgradeBuilder.Rebuild(sourceLease, paths.Staging, stageOptions);
        ZoneTreeFormatUpgradeStage.RemoveSourceCopies(paths.Staging);
        ZoneTreeFormatUpgradeStage.VerifyPublishable(paths.Staging, receipt);
        sourceLease.VerifyUnchanged();
        ZoneTreeFormatUpgradeStage.Publish(paths.Staging, paths.Destination);
        destinationOptions.FaultObserver?.Invoke(CommitStage.UpgradePublished, sourceLease.Position, 0);
        return identity;
    }

    private static StoreIdentity VerifyPublished(string destination, ZoneTreeFormatUpgradeReceipt expected,
        ZoneTreeFormatUpgradeSource source, ZoneTreeStoreOptions options)
    {
        ZoneTreeFormatUpgradeStage.VerifyDirectory(destination);
        var actual = ZoneTreeFormatUpgradeReceiptFile.Read(
            Path.Combine(destination, ZoneTreeFormatUpgradeStage.ReceiptFileName));
        if (actual != expected)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, UpgradePathAmbiguous);
        }
        source.VerifyUnchanged();
        return ZoneTreeFormatUpgradeBuilder.VerifyPublishedTarget(destination, source.Identity,
            source.Position, TargetOptions(options, destination, source.Identity));
    }

    private static ZoneTreeFormatUpgradeReceipt CreateReceipt(UpgradePaths paths,
        ZoneTreeFormatUpgradeSource source) => new(ZoneTreePersistenceFormat.UpgradeReceiptVersion, paths.Source, paths.Destination,
            source.IdentityDigest, source.JournalDigest, source.Identity.NodeId, source.Identity.Incarnation,
            source.Identity.FormatVersion, ZoneTreePersistenceFormat.CurrentDataEpoch);

    private static ZoneTreeStoreOptions TargetOptions(ZoneTreeStoreOptions options, string directory,
        StoreIdentity sourceIdentity) => options with
        {
            Directory = directory,
            Incarnation = sourceIdentity.Incarnation,
            SigningKey = sourceIdentity.SigningKey,
            EmbeddedPointCache = null
        };

    private static void ValidateOptions(ZoneTreeStoreOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Directory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxFrameBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxSnapshotBytes);
        options.EmbeddedPointCache?.Validate();
    }

    private static UpgradePaths NormalizePaths(string source, string destination)
    {
        var sourcePath = Normalize(source);
        var destinationPath = Normalize(destination);
        var stagingPath = destinationPath + ".upgrade";
        var parent = Path.GetDirectoryName(destinationPath);
        if (parent is null || !Directory.Exists(parent) || IsSameOrNested(sourcePath, destinationPath)
            || IsSameOrNested(destinationPath, sourcePath) || IsSameOrNested(sourcePath, stagingPath)
            || IsSameOrNested(stagingPath, sourcePath))
        {
            throw Errors.Fail(ErrorCode.Validation, UpgradePathAmbiguous);
        }
        _ = ZoneTreeFormatUpgradePathSafety.VerifyNoLinks(sourcePath, allowMissingFinal: false);
        _ = ZoneTreeFormatUpgradePathSafety.VerifyNoLinks(destinationPath, allowMissingFinal: true);
        var stagingAttributes = ZoneTreeFormatUpgradePathSafety.VerifyNoLinks(stagingPath, allowMissingFinal: true);
        if (stagingAttributes is { } attributes)
        {
            if ((attributes & FileAttributes.Directory) == 0)
            {
                throw Errors.Fail(ErrorCode.FormatUnsupported, UpgradePathAmbiguous);
            }
            ZoneTreeFormatUpgradeStage.VerifyDirectory(stagingPath);
        }
        return new(sourcePath, destinationPath, stagingPath);
    }

    private static string Normalize(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsSameOrNested(string parent, string candidate)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var relative = Path.GetRelativePath(parent, candidate);
        return relative == "." || !Path.IsPathRooted(relative)
            && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, comparison)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, comparison);
    }

    private const string UpgradePathAmbiguous = "Offline format upgrade paths are not separate, empty and unambiguous directories.";
    private readonly record struct UpgradePaths(string Source, string Destination, string Staging);
}
