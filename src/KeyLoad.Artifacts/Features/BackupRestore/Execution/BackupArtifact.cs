using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Cartograph.Catalog;
using Cartograph.Format;

namespace KeyLoad.Artifacts;

/// <summary>Optional regenerable archive. Cartograph is never used as the transaction journal.</summary>
public static class BackupArtifact
{
    private const FileAttributes NoMatchingAttributes = 0;
    private const int FirstCatalogRecordIndex = 0;
    private const int MinimumFilePieceCount = 1;
    private const int CeilingDivisionAdjustment = 1;
    private const int UnavailableCatalogChecksum = 0;
    private const int CanonicalFileSegmentIndex = 1;
    private const long CatalogRecordOffset = 1L;
    private const long EmptyFileBytes = 0;
    private const long FirstFileOffset = 0;
    private const int MinimumPieceBytes = 1_024;
    private const int MaximumPieceBytes = 1_073_741_824;
    private const string ManifestFileName = "backup.json";
    private const string JournalFileName = "commands.wal";
    private const string IdentityFileName = "identity.json";
    private const string CatalogSourceRoot = "keyload-backup";
    private const string FlatGroupingMode = "flat";
    private const string CanonicalGroupName = "canonical";
    private const string InvalidBackupDirectory = "Pack requires a regular verified KeyLoad backup directory.";
    private const string NonemptyDestination = BackupArtifactStageFileSystem.NonemptyDestinationError;
    internal const string InvalidCatalog = "The artifact is not a canonical KeyLoad backup.";
    internal const string InvalidLength = "The artifact length does not match its catalog.";
    internal static readonly ImmutableArray<string> CanonicalFileNames = [ManifestFileName, JournalFileName, IdentityFileName];

    /// <summary>Packages the canonical backup files into bounded archive pieces.</summary>
    /// <param name="backupDirectory">Directory containing the verified backup files.</param>
    /// <param name="artifactPath">New archive path.</param>
    /// <param name="pieceBytes">Maximum bytes in each file-backed archive piece.</param>
    /// <param name="timeProvider">Borrowed catalog clock; defaults to the system provider.</param>
    public static void Pack(string backupDirectory, string artifactPath, int pieceBytes, TimeProvider? timeProvider = null)
    {
        PackFiles(backupDirectory, artifactPath, pieceBytes, timeProvider ?? TimeProvider.System, CanonicalFileNames, out _);
    }

    internal static void PackFiles(string backupDirectory, string artifactPath, int pieceBytes, TimeProvider clock,
        ImmutableArray<string> requiredNames, out bool created)
    {
        created = false;
        if (pieceBytes < MinimumPieceBytes || pieceBytes > MaximumPieceBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceBytes));
        }
        var files = GetCanonicalFiles(Path.GetFullPath(backupDirectory), requiredNames);
        var catalog = CreateCatalog(files, pieceBytes, clock);
        var writer = new SegmentedArtifactWriter();
        PopulateWriter(writer, catalog, files, pieceBytes);
        using var destination = new FileStream(artifactPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        created = true;
        // Save's file descriptors stream each source through a pooled buffer; payloads are not retained in memory.
        destination.Dispose();
        writer.Save(artifactPath);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(artifactPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        using var flush = new FileStream(artifactPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        flush.Flush(true);
    }

    /// <summary>Packages the exact native catalog backup, requiring its original capture receipt digest.</summary>
    /// <param name="backupDirectory">Actual verified four-file source directory.</param>
    /// <param name="artifactPath">New operation-owned artifact path.</param>
    /// <param name="pieceBytes">Existing native transfer piece bound.</param>
    /// <param name="expectedManifestDigest">Separately retained original capture receipt digest.</param>
    /// <param name="options">Original centrally validated native storage execution options.</param>
    /// <param name="timeProvider">Borrowed artifact catalog clock.</param>
    /// <param name="cancellationToken">Original operation cancellation.</param>
    public static void PackCatalogBackup(string backupDirectory, string artifactPath, int pieceBytes,
        string expectedManifestDigest, Microsoft.Extensions.Options.IOptions<KeyLoad.Storage.ZoneTree.ZoneTreeStorageExecutionOptions> options,
        TimeProvider timeProvider, CancellationToken cancellationToken) =>
        CatalogBackupArtifactOperations.Pack(backupDirectory, artifactPath, pieceBytes, expectedManifestDigest,
            options, timeProvider, cancellationToken);

    /// <summary>Verifies and publishes the exact four-file native archive into an empty destination.</summary>
    /// <param name="artifactPath">Original bounded-piece artifact path.</param>
    /// <param name="destination">Explicit clean operator-owned destination.</param>
    /// <param name="expectedManifestDigest">Separately retained original capture receipt digest.</param>
    /// <param name="options">Original centrally validated native storage execution options.</param>
    /// <param name="cancellationToken">Original operation cancellation.</param>
    public static void UnpackCatalogBackup(string artifactPath, string destination, string expectedManifestDigest,
        Microsoft.Extensions.Options.IOptions<KeyLoad.Storage.ZoneTree.ZoneTreeStorageExecutionOptions> options,
        CancellationToken cancellationToken) =>
        CatalogBackupArtifactOperations.Unpack(artifactPath, destination, expectedManifestDigest, options, cancellationToken);

    private static FileInfo[] GetCanonicalFiles(string root, ImmutableArray<string> requiredNames)
    {
        var files = requiredNames.Select(name => new FileInfo(Path.Combine(root, name))).ToArray();
        if (files.Any(file => !file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != NoMatchingAttributes))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidBackupDirectory);
        }
        return files;
    }

    private static FileCatalog CreateCatalog(FileInfo[] files, int pieceBytes, TimeProvider clock)
    {
        var entries = new List<CatalogEntry>();
        var record = FirstCatalogRecordIndex;
        foreach (var file in files)
        {
            var count = checked((int)Math.Max(MinimumFilePieceCount, (file.Length + pieceBytes - CeilingDivisionAdjustment) / pieceBytes));
            entries.Add(new()
            {
                RelativePath = file.Name,
                Length = file.Length,
                RecordCount = count,
                LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks,
                Checksum = UnavailableCatalogChecksum,
                SegmentIndex = CanonicalFileSegmentIndex,
                RecordIndex = record,
                GlobalIndex = CatalogRecordOffset + record
            });
            record = checked(record + count);
        }
        return new FileCatalog
        {
            SourceRoot = CatalogSourceRoot,
            CreatedUtc = clock.GetUtcNow().UtcDateTime,
            GroupingMode = FlatGroupingMode,
            GroupNames = [CanonicalGroupName],
            Entries = entries
        };
    }

    private static void PopulateWriter(SegmentedArtifactWriter writer, FileCatalog catalog, FileInfo[] files, int pieceBytes)
    {
        writer.AddSegment().AddRecord(catalog.Serialize());
        var segment = writer.AddSegment();
        foreach (var file in files)
        {
            if (file.Length == EmptyFileBytes)
            {
                segment.AddRecord(ReadOnlySpan<byte>.Empty);
            }
            for (var offset = FirstFileOffset; offset < file.Length; offset += pieceBytes)
            {
                segment.AddFileRecord(file.FullName, offset, Math.Min(pieceBytes, file.Length - offset));
            }
        }
    }

    /// <summary>Reads the canonical entry names, lengths and piece counts from an archive.</summary>
    /// <param name="artifactPath">Archive path to inspect.</param>
    /// <returns>Entry metadata in catalog order.</returns>
    public static (string Name, long Bytes, int Pieces)[] Inspect(string artifactPath)
    {
        using var artifact = CatalogedArtifact.Open(artifactPath);
        return artifact.Entries.Select(entry => (entry.RelativePath, entry.Length, entry.RecordCount)).ToArray();
    }

    /// <summary>Extracts a canonical backup archive into an empty destination.</summary>
    /// <param name="artifactPath">Archive path to extract.</param>
    /// <param name="destination">Empty destination directory.</param>
    public static void Unpack(string artifactPath, string destination)
    {
        var destinationState = BackupArtifactStageFileSystem.CaptureDestination(destination, NonemptyDestination);
        BackupArtifactStaging? staging = null;
        Exception? failure = null;
        try
        {
            BackupArtifactFailurePolicy.TryCapture(() =>
            {
                staging = BackupArtifactExtraction.StageArtifact(artifactPath, destinationState);
                staging.Publish();
            }, operationFailure => failure = BackupArtifactFailurePolicy.Combine(failure, operationFailure));
        }
        finally
        {
            if (staging is not null)
            {
                BackupArtifactFailurePolicy.TryCapture(staging.Dispose,
                    cleanupFailure => failure = BackupArtifactFailurePolicy.Combine(failure, cleanupFailure));
            }
        }
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
