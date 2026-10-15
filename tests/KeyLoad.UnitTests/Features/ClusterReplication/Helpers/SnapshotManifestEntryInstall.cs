using System.Text;
using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.ClusterReplication.SnapshotManifestEntryProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class SnapshotManifestEntryInstall
{
    internal static async Task<SnapshotManifestEntryTerminal> RunAsync(SnapshotManifestEntryOwners owner,
        SnapshotManifestEntryState state, CancellationToken token)
    {
        var source = owner.Open(source: true);
        var target = owner.Open(source: false);
        var offset = target.Snapshots.Begin(state.Snapshot);
        await Assert.That(offset).IsEqualTo((long)state.Prefix.Length);
        await Assert.That(target.Snapshots.Append(state.Snapshot.TransferId, 0, state.Prefix)).IsEqualTo(offset);
        while (offset < state.Snapshot.Length)
        {
            token.ThrowIfCancellationRequested();
            offset = target.Snapshots.Append(state.Snapshot.TransferId, offset,
                source.Snapshots.ReadChunk(state.Snapshot.TransferId, offset, target.Configuration.SnapshotChunkBytes));
        }
        await Assert.That(target.Snapshots.Complete(state.Snapshot.TransferId)).IsEqualTo(state.Snapshot);
        await SnapshotManifestEntryAssertions.DocumentAsync(target, P.SnapshotCut);
        await SnapshotManifestEntryAssertions.ReceiptAsync(target, ReplicaCrashModel.Operation(P.SnapshotCut), P.SnapshotCut);
        await Assert.That(target.HasObsoleteRecord()).IsFalse();
        var apply = owner.StartMaterializer();
        apply.Commit(P.TailCut);
        await apply.WaitForApplyAsync(P.TailCut, token);
        await SnapshotManifestEntryAssertions.DocumentAsync(target, P.TailCut);
        var command = new CommandRequest(Guid.NewGuid(), ReplicaCrashModel.Document.Partition,
            [new PutDocument(P.Collection, P.Document, P.HealthyJson, P.TailCut - P.RevisionOffset)]);
        var healthy = target.Database.NormalizeOperation(new(command.CommandId, OperationKind.Batch,
            ReplicaCrashModel.PrincipalId, target.Database.EvaluationClock.GetUtcNow(),
            Encoding.UTF8.GetString(JsonDefaults.Serialize(command))));
        target.Log.Append([new(P.HealthyCut, P.Term, healthy)]);
        apply.Commit(P.HealthyCut);
        await apply.WaitForApplyAsync(P.HealthyCut, token);
        await SnapshotManifestEntryAssertions.DocumentAsync(target, P.HealthyCut);
        await SnapshotManifestEntryAssertions.AllReceiptsAsync(target, healthy);
        var terminal = new SnapshotManifestEntryTerminal(healthy,
            SnapshotManifestEntryAssertions.Bytes(target.Canonical), target.Canonical.Position,
            SnapshotManifestEntryAssertions.Bytes(target.ReplicaStore), target.ReplicaStore.Position,
            ReplicaProtocolCodec.Serialize(target.Log.State), target.Canonical.Identity);
        var failures = new List<Exception>();
        await owner.CloseAsync(failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return terminal;
    }
}
