using System.Text;
using KeyLoad.CrashHost;
using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal static class ReplicaSnapshotInstallFencingHealthy
{
    internal static async Task RunAsync(ReplicaCrashNode node, ReplicaMaterializer materializer, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var reference = ReplicaCrashModel.Document;
        var request = new CommandRequest(id, reference.Partition,
            [new PutDocument(reference.Collection, reference.Id, ReplicaSnapshotInstallFencingProtocol.HealthyJson,
                ReplicaSnapshotInstallFencingProtocol.PreviousRevision)]);
        var operation = node.Database.NormalizeOperation(new(id, OperationKind.Batch, ReplicaCrashModel.PrincipalId,
            DateTimeOffset.UnixEpoch.AddTicks(ReplicaSnapshotInstallFencingProtocol.HealthyCut),
            Encoding.UTF8.GetString(JsonDefaults.Serialize(request))));
        node.Log.Append([new(ReplicaSnapshotInstallFencingProtocol.HealthyCut,
            ReplicaSnapshotInstallFencingProtocol.InitialTerm, operation)]);
        materializer.Commit(ReplicaSnapshotInstallFencingProtocol.HealthyCut);
        await ReplicaProcessAssertions.WaitAsync(materializer, ReplicaSnapshotInstallFencingProtocol.HealthyCut, token);
        var document = node.Database.GetDocument(ReplicaCrashModel.PrincipalId, reference, cancellationToken: token);
        var expected = new DocumentResult(reference, ReplicaSnapshotInstallFencingProtocol.HealthyRevision,
            ReplicaSnapshotInstallFencingProtocol.HealthyJson, false, []);
        await Assert.That(JsonDefaults.Serialize(document).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await ReplicaPrefixGcReceiptAssertions.VerifyOperationAsync(node, operation, ReplicaSnapshotInstallFencingProtocol.HealthyCut);
        await ReplicaPrefixGcReceiptAssertions.AllOriginalAsync(node, ReplicaSnapshotInstallFencingProtocol.TailCut);
    }
}
