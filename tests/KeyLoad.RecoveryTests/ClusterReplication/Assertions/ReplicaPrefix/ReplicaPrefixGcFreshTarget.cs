using KeyLoad.CrashHost;
using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal static class ReplicaPrefixGcFreshTarget
{
    internal const string DirectoryName = "fresh-prefix-recovery";
    private const long EmptyCut = 0;
    private const int HealthyCut = 6;
    private const long HealthyRevision = 5;
    private const long RevisionStep = 1;
    private const string HealthyJson = "{\"value\":\"after-gc\"}";
    private static readonly Guid HealthyCommand = new("a0692f31-7df7-454f-b53d-43c8937d3544");

    internal static async Task VerifyAsync(string root, ReplicaCrashNode source, ReplicaSnapshot image,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(root, DirectoryName);
        await ReplicaPrefixGcOwners.NodeAsync(directory, image.Incarnation, async target =>
        {
            await Assert.That(target.Database.LastApplied).IsEqualTo(EmptyCut);
            target.Log.SaveTermAndVote(ReplicaPrefixGcCrashContract.InitialTerm, null);
            var offset = target.Snapshots.Begin(image);
            await Assert.That(offset).IsEqualTo(EmptyCut);
            while (offset < image.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                offset = target.Snapshots.Append(image.TransferId, offset,
                    source.Snapshots.ReadChunk(image.TransferId, offset, target.Configuration.SnapshotChunkBytes));
            }
            await Assert.That(target.Snapshots.Complete(image.TransferId)).IsEqualTo(image);
            await ReplicaPrefixGcReceiptAssertions.AllOriginalAsync(target, ReplicaPrefixGcCrashContract.SnapshotCut);
            var tail = source.Log.ReadEntry(ReplicaPrefixGcCrashContract.TailCut)!;
            target.Log.Append([tail]);
            await ReplicaPrefixGcOwners.MaterializerAsync(target, async materializer =>
            {
                materializer.Commit(ReplicaPrefixGcCrashContract.TailCut);
                await ReplicaProcessAssertions.WaitAsync(materializer, ReplicaPrefixGcCrashContract.TailCut, cancellationToken);
                await ReplicaPrefixGcReceiptAssertions.DocumentAsync(target, ReplicaPrefixGcCrashContract.TailCut);
                await ReplicaPrefixGcReceiptAssertions.AllOriginalAsync(target, ReplicaPrefixGcCrashContract.TailCut);
                var operation = HealthyOperation(target);
                target.Log.Append([new(HealthyCut, ReplicaPrefixGcCrashContract.InitialTerm, operation)]);
                materializer.Commit(HealthyCut);
                await ReplicaProcessAssertions.WaitAsync(materializer, HealthyCut, cancellationToken);
                await HealthyAsync(target);
            });
        }, source.Canonical.Identity.SigningKey);
        await ReplicaPrefixGcOwners.NodeAsync(directory, image.Incarnation, reopened =>
            ReplicaPrefixGcOwners.MaterializerAsync(reopened, async recovered =>
            {
                await ReplicaProcessAssertions.WaitAsync(recovered, HealthyCut, cancellationToken);
                await HealthyAsync(reopened);
            }), source.Canonical.Identity.SigningKey);
    }

    private static async Task HealthyAsync(ReplicaCrashNode node)
    {
        var document = node.Database.GetDocument(ReplicaCrashModel.PrincipalId, ReplicaCrashModel.Document);
        await Assert.That(node.Database.LastApplied).IsEqualTo((long)HealthyCut);
        await Assert.That(node.Log.State.CommittedIndex).IsEqualTo((long)HealthyCut);
        await Assert.That(document).IsNotNull();
        await Assert.That(document!.Revision).IsEqualTo(HealthyRevision);
        await Assert.That(document.Json).IsEqualTo(HealthyJson);
        var expected = new DocumentResult(ReplicaCrashModel.Document, HealthyRevision, HealthyJson, false, []);
        await Assert.That(JsonDefaults.Serialize(document).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await ReplicaPrefixGcReceiptAssertions.VerifyOperationAsync(node, HealthyOperation(node), HealthyCut);
    }

    private static ReplicatedOperation HealthyOperation(ReplicaCrashNode node)
    {
        var command = new CommandRequest(HealthyCommand, ReplicaCrashModel.Document.Partition,
            [new PutDocument(ReplicaCrashModel.Document.Collection, ReplicaCrashModel.Document.Id, HealthyJson, HealthyRevision - RevisionStep)]);
        return node.Database.NormalizeOperation(new(HealthyCommand, OperationKind.Batch,
            ReplicaCrashModel.PrincipalId, DateTimeOffset.UnixEpoch.AddTicks(HealthyCut),
            System.Text.Encoding.UTF8.GetString(JsonDefaults.Serialize(command))));
    }
}
