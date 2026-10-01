using System.Buffers;
using System.Text.Json;
using DotNext.IO;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Replication;

public enum SnapshotInstallStage { IntentPublished, SnapshotVerified, RejectedFilePublished, IntentCleared }

public sealed class ReplicatedStateMachine : IStateMachine, IAsyncDisposable
{
    private sealed record IncomingIntent(int Version, Guid Incarnation, long Index, long Term);
    private readonly DatabaseEngine database;
    private readonly DirectoryInfo location;
    private readonly CanonicalMaterializer materializer;
    private readonly Action<SnapshotInstallStage, long>? faultObserver;
    private string IntentPath => Path.Combine(location.FullName, "incoming");
    public ReplicatedStateMachine(DatabaseEngine database, DirectoryInfo location, int snapshotThreshold = 1_024,
        Action<SnapshotInstallStage, long>? faultObserver = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(snapshotThreshold, 2);
        this.database = database; this.location = location; this.faultObserver = faultObserver;
        materializer = new(database, location, snapshotThreshold, index => faultObserver?.Invoke(SnapshotInstallStage.SnapshotVerified, index));
    }
    public ISnapshot? Snapshot => ((IStateMachine)materializer).Snapshot;
    public ValueTask RestoreAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); RecoverIncomingIntent();
        // This startup path runs before the WAL/raft host owns the state machine. Temporary files are never snapshots.
        foreach (var path in Directory.EnumerateFiles(location.FullName, "*.tmp", SearchOption.TopDirectoryOnly))
        {
            token.ThrowIfCancellationRequested();
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) File.Delete(path);
        }
        return materializer.RestoreAsync(token);
    }
    public ValueTask<long> ApplyAsync(LogEntry entry, CancellationToken token)
        => entry.IsSnapshot ? InstallIncomingAsync(entry, token) : ((IStateMachine)materializer).ApplyAsync(entry, token);
    private async ValueTask<long> InstallIncomingAsync(LogEntry entry, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (entry.Index < 1 || entry.Term < 0) throw Errors.Fail(ErrorCode.Corruption, "The incoming snapshot position is invalid.");
        var intent = new IncomingIntent(1, database.Store.Identity.Incarnation, entry.Index, entry.Term);
        WriteIntent(intent); materializer.ValidatedIncomingIndex = null;
        try
        {
            faultObserver?.Invoke(SnapshotInstallStage.IntentPublished, entry.Index);
            var applied = await ((IStateMachine)materializer).ApplyAsync(entry, token).ConfigureAwait(false);
            File.Delete(IntentPath); faultObserver?.Invoke(SnapshotInstallStage.IntentCleared, entry.Index);
            return applied;
        }
        catch
        {
            // The provider may rename its partial file in a finally block. Never expose that file on reopening.
            // A fully verified image must survive an interrupted canonical install so recovery can finish it.
            faultObserver?.Invoke(SnapshotInstallStage.RejectedFilePublished, entry.Index);
            if (materializer.ValidatedIncomingIndex != entry.Index) File.Delete(SnapshotPath(intent));
            File.Delete(IntentPath);
            throw;
        }
    }
    private string SnapshotPath(IncomingIntent intent) => Path.Combine(location.FullName,
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{intent.Index}-{intent.Term}"));
    private void WriteIntent(IncomingIntent intent)
    {
        if (File.Exists(IntentPath)) throw Errors.Fail(ErrorCode.RecoveryRequired, "A previous snapshot installation requires recovery.");
        var temporary = Path.Combine(location.FullName, "intent" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4_096, FileOptions.WriteThrough))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                file.Write(JsonDefaults.Serialize(intent)); file.Flush(true);
            }
            File.Move(temporary, IntentPath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private void RecoverIncomingIntent()
    {
        if (!File.Exists(IntentPath)) return;
        IncomingIntent intent;
        try
        {
            if (new FileInfo(IntentPath).Length is < 1 or > 1_024) throw Errors.Fail(ErrorCode.Corruption, "The snapshot intent is invalid.");
            intent = JsonDefaults.Deserialize<IncomingIntent>(File.ReadAllBytes(IntentPath));
            if (intent.Version != 1 || intent.Incarnation != database.Store.Identity.Incarnation || intent.Index < 1 || intent.Term < 0)
                throw Errors.Fail(ErrorCode.Corruption, "The snapshot intent scope is invalid.");
        }
        catch (JsonException) { throw Errors.Fail(ErrorCode.Corruption, "The snapshot intent contains invalid JSON."); }
        var path = SnapshotPath(intent); var valid = false;
        if (File.Exists(path))
        {
            try
            {
                var cut = database.Store.VerifySnapshot(path);
                valid = cut.Incarnation == intent.Incarnation && cut.AppliedPosition == intent.Index;
            }
            catch (KeyLoadException exception) when (exception.Code is ErrorCode.Corruption or ErrorCode.FormatUnsupported or ErrorCode.TokenInvalidated or ErrorCode.ResourceExhausted) { }
            catch (JsonException) { }
        }
        if (!valid)
        {
            // Once canonical apply reached this cut, losing its corresponding snapshot is corruption, not a torn upload.
            if (database.LastApplied >= intent.Index)
                throw Errors.Fail(ErrorCode.Corruption, "A materialized snapshot installation has no complete verified image.");
            File.Delete(path);
        }
        File.Delete(IntentPath);
    }
    public ValueTask ReclaimGarbageAsync(long watermark, CancellationToken token)
        => ((IStateMachine)materializer).ReclaimGarbageAsync(watermark, token);
    public ValueTask DisposeAsync() => materializer.DisposeAsync();

    private sealed class CanonicalMaterializer(DatabaseEngine database, DirectoryInfo location, int snapshotThreshold, Action<long> verified)
        : SimpleStateMachine(PrivateLocation(location))
    {
        private readonly string snapshotDirectory = location.FullName;
        private long snapshotCut;
        public long? ValidatedIncomingIndex { get; set; }
        protected override ValueTask<bool> ApplyAsync(LogEntry entry, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (entry.Index <= database.LastApplied) return ValueTask.FromResult(false);
            if (entry.IsConfiguration || entry.Length is 0)
                database.Store.Commit((tx, _) => { tx.PutRecord(KeySpace.Applied, entry.Index); return true; });
            else if (entry.TryGetPayload(out var payload) && payload.Length <= database.Limits.MaxBatchBytes + 65_536)
                database.Apply(JsonDefaults.Deserialize<ReplicatedOperation>(payload.ToArray()), entry.Index);
            else throw Errors.Fail(ErrorCode.Corruption, "A replication entry is invalid or exceeds its budget.");
            snapshotCut = entry.Index;
            return ValueTask.FromResult(entry.Index % snapshotThreshold == 0);
        }
        protected override async ValueTask PersistAsync(IAsyncBinaryWriter writer, CancellationToken token)
        {
            var temporary = Path.Combine(snapshotDirectory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                database.Store.CreateSnapshot(temporary, snapshotCut);
                await using var file = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read, 65_536,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await writer.CopyFromAsync(file, token: token).ConfigureAwait(false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        protected override ValueTask RestoreAsync(FileInfo snapshotFile, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var index = long.Parse(snapshotFile.Name.Split('-')[0], System.Globalization.CultureInfo.InvariantCulture);
            var cut = database.Store.VerifySnapshot(snapshotFile.FullName);
            if (cut.Incarnation != database.Store.Identity.Incarnation || cut.AppliedPosition != index)
                throw Errors.Fail(ErrorCode.TokenInvalidated, "The persistent replica snapshot scope is invalid.");
            ValidatedIncomingIndex = index; verified(index);
            // A durable materialization may already include commits newer than the last completed snapshot.
            if (database.LastApplied < index) database.Store.InstallSnapshot(snapshotFile.FullName, index);
            return ValueTask.CompletedTask;
        }
        private static DirectoryInfo PrivateLocation(DirectoryInfo directory)
        {
            directory.Create();
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory.FullName,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return directory;
        }
    }
}
