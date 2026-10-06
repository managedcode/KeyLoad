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
        if (pieceBytes < MinimumPieceBytes || pieceBytes > MaximumPieceBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceBytes));
        }
        var files = GetCanonicalFiles(Path.GetFullPath(backupDirectory));
        var catalog = CreateCatalog(files, pieceBytes, timeProvider ?? TimeProvider.System);
        var writer = new SegmentedArtifactWriter();
        PopulateWriter(writer, catalog, files, pieceBytes);
        using var destination = new FileStream(artifactPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
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

    private static FileInfo[] GetCanonicalFiles(string root)
    {
        var files = CanonicalFileNames.Select(name => new FileInfo(Path.Combine(root, name))).ToArray();
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
            staging = BackupArtifactExtraction.StageArtifact(artifactPath, destinationState);
            staging.Publish();
        }
        catch (Exception operationFailure)
        {
            failure = operationFailure;
        }
        try
        {
            staging?.Dispose();
        }
        catch (Exception cleanupFailure) when (failure is not null)
        {
            throw new AggregateException(failure, cleanupFailure);
        }
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
