using Cartograph.Catalog;
using Cartograph.Format;

namespace KeyLoad.Artifacts;

/// <summary>Optional regenerable archive. Cartograph is never used as the transaction journal.</summary>
public static class BackupArtifact
{
    public static void Pack(string backupDirectory, string artifactPath, int pieceBytes = 268_435_456)
    {
        if (pieceBytes < 1_024 || pieceBytes > 1_073_741_824) throw new ArgumentOutOfRangeException(nameof(pieceBytes));
        var root = Path.GetFullPath(backupDirectory);
        var files = new[] { "backup.json", "commands.wal", "identity.json" }.Select(name => new FileInfo(Path.Combine(root, name))).ToArray();
        if (files.Any(f => !f.Exists || (f.Attributes & FileAttributes.ReparsePoint) != 0))
            throw Errors.Fail(ErrorCode.Validation, "Pack requires a regular verified KeyLoad backup directory.");
        var entries = new List<CatalogEntry>();
        var record = 0;
        foreach (var file in files)
        {
            var count = checked((int)Math.Max(1, (file.Length + pieceBytes - 1) / pieceBytes));
            entries.Add(new() { RelativePath = file.Name, Length = file.Length, RecordCount = count, LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks,
                Checksum = 0, SegmentIndex = 1, RecordIndex = record, GlobalIndex = 1L + record });
            record = checked(record + count);
        }
        var catalog = new FileCatalog { SourceRoot = "keyload-backup", CreatedUtc = DateTime.UtcNow,
            GroupingMode = "flat", GroupNames = ["canonical"], Entries = entries };
        var writer = new SegmentedArtifactWriter();
        writer.AddSegment().AddRecord(catalog.Serialize());
        var segment = writer.AddSegment();
        foreach (var file in files)
        {
            if (file.Length == 0) segment.AddRecord(ReadOnlySpan<byte>.Empty);
            for (long offset = 0; offset < file.Length; offset += pieceBytes)
                segment.AddFileRecord(file.FullName, offset, Math.Min(pieceBytes, file.Length - offset));
        }
        using var destination = new FileStream(artifactPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        // Save's file descriptors stream each source through a pooled buffer; payloads are not retained in memory.
        destination.Dispose();
        writer.Save(artifactPath);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(artifactPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var flush = new FileStream(artifactPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None); flush.Flush(true);
    }
    public static (string Name, long Bytes, int Pieces)[] Inspect(string artifactPath)
    {
        using var artifact = CatalogedArtifact.Open(artifactPath);
        return artifact.Entries.Select(entry => (entry.RelativePath, entry.Length, entry.RecordCount)).ToArray();
    }
    public static void Unpack(string artifactPath, string destination)
    {
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw Errors.Fail(ErrorCode.Conflict, "Artifact extraction requires an empty destination.");
        using var artifact = CatalogedArtifact.Open(artifactPath);
        if (!artifact.Entries.Select(e => e.RelativePath).Order().SequenceEqual(new[] { "backup.json", "commands.wal", "identity.json" }))
            throw Errors.Fail(ErrorCode.Validation, "The artifact is not a canonical KeyLoad backup.");
        Directory.CreateDirectory(destination);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destination,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        foreach (var entry in artifact.Entries)
        {
            using var file = new FileStream(Path.Combine(destination, entry.RelativePath), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            if (artifact.CopyTo(entry, file) != entry.Length) throw Errors.Fail(ErrorCode.Corruption, "The artifact length does not match its catalog.");
            file.Flush(true);
        }
        // The storage restore command verifies the backup's SHA-256 manifest before accepting it.
    }
}
