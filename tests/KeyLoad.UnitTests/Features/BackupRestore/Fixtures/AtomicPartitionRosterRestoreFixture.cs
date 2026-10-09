using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class AtomicPartitionRosterRestoreFixture
{
    private const string DirectoryPrefix = "keyload-roster-restoration-";
    private const string FirstBackup = "first-backup";
    private const string SecondBackup = "second-backup";
    private const string FirstTarget = "first-target";
    private const string SecondTarget = "second-target";
    private const string GuidFormat = "N";
    private const string CorruptBackup = "corrupt-backup";
    private const string RejectedTarget = "rejected-target";
    private const string StagingPattern = ".keyload-restore-*";
    internal const string Principal = "root";
    internal const string SeedId = "historical";
    internal const string NewId = "new-incarnation";
    internal const string SecondId = "second-restoration";
    internal const string LiteralJson = "{\"restored\":true}";
    internal const long SourceApplied = 7;
    internal const long LocalApplied = 0;
    internal const long NewApplied = 1;
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
    internal AtomicPartitionRosterFixture Source { get; private set; } = null!;
    internal ZoneTreeStore Target { get; private set; } = null!;
    internal DatabaseEngine Database { get; private set; } = null!;
    internal ReplicatedOperation Original { get; private set; } = null!;
    internal OperationResult OriginalResult { get; private set; } = null!;
    internal byte[] OriginalOutcome { get; private set; } = null!;
    internal byte[] OriginalRoster { get; private set; } = null!;
    private string targetPath = null!;
    private Dictionary<string, string> firstArchive = null!;
    private long sourceCut;

    internal static async Task RunAsync(Func<AtomicPartitionRosterRestoreFixture, Task> operation)
    {
        var fixture = new AtomicPartitionRosterRestoreFixture();
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(fixture.Initialize, failures);
        if (failures.Count == 0)
        { await ServerFailureObserver.ObserveAsync(() => operation(fixture), failures); }
        if (fixture.Target is { } target)
        { ServerFailureObserver.Observe(target.Dispose, failures); }
        if (fixture.Source is { } source)
        { ServerFailureObserver.Observe(source.Dispose, failures); }
        if (failures.Count == 0)
        { ServerFailureObserver.Observe(() => Directory.Delete(fixture.Root, true), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void Initialize()
    {
        Directory.CreateDirectory(Root);
        Source = new();
        Source.ConfigureGraph();
        Original = Operation(Source.Database, SeedId);
        OriginalResult = Source.Database.Apply(Original, SourceApplied);
        BackupIndexedDocumentDedupFixture.RequireSuccess(OriginalResult);
        OriginalRoster = Source.ReadEntryBytes(AtomicPartitionRosterFixture.Source)!;
        OriginalOutcome = Source.Store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.PartitionKey(
            AtomicPartitionRosterFixture.Source, Principal, Original.Id)))!;
        var backup = Path.Combine(Root, FirstBackup);
        sourceCut = Source.Store.CreateBackup(backup);
        firstArchive = ArchiveDigests(backup);
        targetPath = Path.Combine(Root, FirstTarget);
        ZoneTreeStore.Restore(backup, targetPath, UnitExecutionOptions.StorageExecution());
        Open();
    }

    internal static ReplicatedOperation Operation(DatabaseEngine database, string documentId)
    {
        var id = Guid.NewGuid();
        var body = new CommandRequest(id, AtomicPartitionRosterFixture.Source,
            [new PutDocument(AtomicPartitionRosterFixture.Collection, documentId, LiteralJson)]);
        return new(id, OperationKind.Batch, Principal, database.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(body, JsonDefaults.Options));
    }

    internal void Reopen()
    { Target.Dispose(); Target = null!; Open(); }

    internal void RestoreAgain()
    {
        var backup = Path.Combine(Root, SecondBackup);
        Target.CreateBackup(backup);
        Target.Dispose();
        Target = null!;
        targetPath = Path.Combine(Root, SecondTarget);
        ZoneTreeStore.Restore(backup, targetPath, UnitExecutionOptions.StorageExecution());
        Open();
    }

    private void Open()
    {
        Target = new(new(targetPath), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        Database = BackupIndexedDocumentDedupFixture.CreateDatabase(Target);
    }

    internal byte[] ReadOriginalOutcome() => Target.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.PartitionKey(
        AtomicPartitionRosterFixture.Source, Principal, Original.Id)))!;

    internal byte[] ReadRoster() => Target.Read(view => view.ReadOwnedValue(
        AtomicPartitionRosterRestoreOriginSerialization.EntryKey(AtomicPartitionRosterFixture.Source)))!;

    internal async Task RequireCorruptBackupRejectedAsync()
    {
        var backup = Path.Combine(Root, CorruptBackup);
        var destination = Path.Combine(Root, RejectedTarget);
        Target.CreateBackup(backup);
        var archive = ArchiveDigests(backup);
        var position = Target.Position;
        var digest = AtomicPartitionRosterRestoreAssertions.CanonicalDigest(Target);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            ZoneTreeStore.Restore(backup, destination, UnitExecutionOptions.StorageExecution()));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(Directory.Exists(destination)).IsFalse();
        await Assert.That(Directory.EnumerateDirectories(Root, StagingPattern).Any()).IsFalse();
        await Assert.That(Target.Position).IsEqualTo(position);
        await Assert.That(AtomicPartitionRosterRestoreAssertions.CanonicalDigest(Target)).IsEqualTo(digest);
        var after = ArchiveDigests(backup);
        await Assert.That(after.Count).IsEqualTo(archive.Count);
        foreach (var entry in archive)
        { await Assert.That(after[entry.Key]).IsEqualTo(entry.Value); }
    }

    internal async Task AssertArchiveUnchangedAsync()
    {
        var after = ArchiveDigests(Path.Combine(Root, FirstBackup));
        await Assert.That(after.Count).IsEqualTo(firstArchive.Count);
        foreach (var entry in firstArchive)
        { await Assert.That(after[entry.Key]).IsEqualTo(entry.Value); }
        await Assert.That(Source.Store.Position).IsEqualTo(sourceCut);
    }

    private static Dictionary<string, string> ArchiveDigests(string directory)
        => Directory.EnumerateFiles(directory).ToDictionary(path => Path.GetFileName(path)!, path =>
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }, StringComparer.Ordinal);
}
