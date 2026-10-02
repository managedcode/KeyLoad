using System.Collections.Immutable;
using Cartograph.Catalog;
using Cartograph.Format;

namespace KeyLoad.Artifacts;

/// <summary>Optional regenerable archive. Cartograph is never used as the transaction journal.</summary>
public static class BackupArtifact
{
    private const int DefaultPieceBytes = 268_435_456;
    private const int MinimumPieceBytes = 1_024;
    private const int MaximumPieceBytes = 1_073_741_824;
    private const string ManifestFileName = "backup.json";
    private const string JournalFileName = "commands.wal";
    private const string IdentityFileName = "identity.json";
    private const string CatalogSourceRoot = "keyload-backup";
    private const string FlatGroupingMode = "flat";
    private const string CanonicalGroupName = "canonical";
    private const string InvalidBackupDirectory = "Pack requires a regular verified KeyLoad backup directory.";
    private const string NonemptyDestination = "Artifact extraction requires an empty destination.";
    private const string InvalidCatalog = "The artifact is not a canonical KeyLoad backup.";
    private const string InvalidLength = "The artifact length does not match its catalog.";
    private static readonly ImmutableArray<string> CanonicalFileNames = [ManifestFileName, JournalFileName, IdentityFileName];

    /// <summary>Packages the canonical backup files into bounded archive pieces.</summary>
    /// <param name="backupDirectory">Directory containing the verified backup files.</param>
    /// <param name="artifactPath">New archive path.</param>
    /// <param name="pieceBytes">Maximum bytes in each file-backed archive piece.</param>
    public static void Pack(string backupDirectory, string artifactPath, int pieceBytes = DefaultPieceBytes)
    {
        if (pieceBytes < MinimumPieceBytes || pieceBytes > MaximumPieceBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceBytes));
        }
        var files = GetCanonicalFiles(Path.GetFullPath(backupDirectory));
        var catalog = CreateCatalog(files, pieceBytes);
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
        if (files.Any(file => !file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidBackupDirectory);
        }
        return files;
    }

    private static FileCatalog CreateCatalog(FileInfo[] files, int pieceBytes)
    {
        var entries = new List<CatalogEntry>();
        var record = 0;
        foreach (var file in files)
        {
            var count = checked((int)Math.Max(1, (file.Length + pieceBytes - 1) / pieceBytes));
            entries.Add(new()
            {
                RelativePath = file.Name,
                Length = file.Length,
                RecordCount = count,
                LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks,
                Checksum = 0,
                SegmentIndex = 1,
                RecordIndex = record,
                GlobalIndex = 1L + record
            });
            record = checked(record + count);
        }
        return new FileCatalog
        {
            SourceRoot = CatalogSourceRoot,
            CreatedUtc = TimeProvider.System.GetUtcNow().UtcDateTime,
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
            if (file.Length == 0)
            {
                segment.AddRecord(ReadOnlySpan<byte>.Empty);
            }
            for (long offset = 0; offset < file.Length; offset += pieceBytes)
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
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        {
            throw Errors.Fail(ErrorCode.Conflict, NonemptyDestination);
        }
        using var artifact = CatalogedArtifact.Open(artifactPath);
        if (!artifact.Entries.Select(entry => entry.RelativePath).Order().SequenceEqual(CanonicalFileNames))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidCatalog);
        }
        Directory.CreateDirectory(destination);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        foreach (var entry in artifact.Entries)
        {
            using var file = new FileStream(Path.Combine(destination, entry.RelativePath), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            if (artifact.CopyTo(entry, file) != entry.Length)
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidLength);
            }
            file.Flush(true);
        }
        // The storage restore command verifies the backup's SHA-256 manifest before accepting it.
    }
}
