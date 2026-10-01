using System.Buffers;
using DotNext.IO;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Replication;

public sealed class ReplicatedStateMachine(DatabaseEngine database, DirectoryInfo location, int snapshotThreshold = 1_024)
    : SimpleStateMachine(PrivateLocation(location))
{
    private readonly string snapshotDirectory = location.FullName;
    private long snapshotCut;
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
