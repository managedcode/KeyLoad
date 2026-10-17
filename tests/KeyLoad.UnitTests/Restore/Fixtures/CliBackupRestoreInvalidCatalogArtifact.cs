using Cartograph.Catalog;
using Cartograph.Format;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CliBackupRestoreInvalidCatalogArtifact
{
    private const int CatalogSegmentIndex = 0;
    private const int ContentSegmentIndex = 1;
    private const int FirstContentRecordIndex = 0;
    private const long FirstGlobalContentIndex = 1;
    private const long FirstByteOffset = 0;
    private const long EmptyFileLength = 0;
    private const long NoLengthAdjustment = 0;
    private const int MinimumRecordCount = 1;
    private const int CeilingDivisionAdjustment = 1;
    private const int MissingChecksum = 0;
    private const string ManifestFileName = "backup.json";
    private const string JournalFileName = "commands.wal";
    private const string IdentityFileName = "identity.json";
    private const string GroupingModeName = "flat";
    private const string ContentGroupName = "canonical";
    private const string InvalidIdentityEntryName = "unexpected-identity.json";
    private static readonly string[] CanonicalNames = [ManifestFileName, JournalFileName, IdentityFileName];

    internal static void Create(string backupDirectory, string artifactPath, int pieceBytes)
        => CreateCore(backupDirectory, artifactPath, pieceBytes, noncanonicalIdentity: true,
            mismatchedEntryName: null, declaredLengthAdjustment: NoLengthAdjustment);

    internal static void CreateWithLengthMismatch(string backupDirectory, string artifactPath,
        int pieceBytes, string mismatchedEntryName, long declaredLengthAdjustment)
        => CreateCore(backupDirectory, artifactPath, pieceBytes, noncanonicalIdentity: false,
            mismatchedEntryName: mismatchedEntryName, declaredLengthAdjustment: declaredLengthAdjustment);

    private static void CreateCore(string backupDirectory, string artifactPath, int pieceBytes,
        bool noncanonicalIdentity, string? mismatchedEntryName, long declaredLengthAdjustment)
    {
        var files = CanonicalNames.Select(name => new FileInfo(Path.Combine(backupDirectory, name))).ToArray();
        var catalog = CreateCatalog(files, pieceBytes, noncanonicalIdentity, mismatchedEntryName, declaredLengthAdjustment);
        var writer = new SegmentedArtifactWriter();
        writer.AddSegment().AddRecord(catalog.Serialize());
        AddFileRecords(writer.AddSegment(), files, pieceBytes);
        writer.Save(artifactPath);
    }

    private static FileCatalog CreateCatalog(FileInfo[] files, int pieceBytes,
        bool noncanonicalIdentity, string? mismatchedEntryName, long declaredLengthAdjustment)
    {
        var entries = new List<CatalogEntry>(files.Length);
        var recordIndex = FirstContentRecordIndex;
        foreach (var file in files)
        {
            var count = checked((int)Math.Max(MinimumRecordCount,
                (file.Length + pieceBytes - CeilingDivisionAdjustment) / pieceBytes));
            entries.Add(CreateEntry(file, recordIndex, count, noncanonicalIdentity, mismatchedEntryName, declaredLengthAdjustment));
            recordIndex = checked(recordIndex + count);
        }
        return new FileCatalog
        {
            SourceRoot = Path.GetFullPath(Path.GetDirectoryName(files[CatalogSegmentIndex].FullName)!),
            CreatedUtc = TimeProvider.System.GetUtcNow().UtcDateTime,
            GroupingMode = GroupingModeName,
            GroupNames = [ContentGroupName],
            Entries = entries
        };
    }

    private static CatalogEntry CreateEntry(FileInfo file, int recordIndex, int count,
        bool noncanonicalIdentity, string? mismatchedEntryName, long declaredLengthAdjustment)
    {
        var relativePath = noncanonicalIdentity && file.Name == CanonicalNames[^1]
            ? InvalidIdentityEntryName
            : file.Name;
        var length = file.Name == mismatchedEntryName
            ? checked(file.Length + declaredLengthAdjustment)
            : file.Length;
        return new CatalogEntry
        {
            RelativePath = relativePath,
            Length = length,
            RecordCount = count,
            LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks,
            Checksum = MissingChecksum,
            SegmentIndex = ContentSegmentIndex,
            RecordIndex = recordIndex,
            GlobalIndex = FirstGlobalContentIndex + recordIndex
        };
    }

    private static void AddFileRecords(SegmentBuilder segment, FileInfo[] files, int pieceBytes)
    {
        foreach (var file in files)
        {
            if (file.Length == EmptyFileLength)
            {
                segment.AddRecord(ReadOnlySpan<byte>.Empty);
            }
            for (var offset = FirstByteOffset; offset < file.Length; offset += pieceBytes)
            {
                segment.AddFileRecord(file.FullName, offset, Math.Min(pieceBytes, file.Length - offset));
            }
        }
    }
}
