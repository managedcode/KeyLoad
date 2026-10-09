using System.Text;
using KeyLoad.CrashHost;
using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

/// <summary>Original published-image refusal followed by exact stopped-image repair and a genuine native continuation.</summary>
internal static class ReplicaSnapshotDamageRecovery
{
    internal const string SavedImageSuffix = ".original-before-damage";
    private const int TailCut = 5;
    private const int HealthyCut = 6;
    private const int HealthyRevision = 5;
    private const int InstalledReadGeneration = 1;
    private const int NativeTerm = 1;
    private const string HealthyJson = "{\"value\":\"after-image-repair\"}";
    private static readonly Guid HealthyCommand = new("f6a45fa1-6ac0-4c47-a92c-f73fc603c905");

    internal static Task RefuseAsync(ReplicaProcessTrial trial, ReplicaSnapshot published, bool missing)
        => ReplicaPrefixGcOwners.NodeAsync(trial.DirectoryPath, trial.Incarnation, async reopened =>
        {
            if (missing)
            { Assert.ThrowsExactly<FileNotFoundException>(reopened.Snapshots.Recover); }
            else
            { await Assert.That(Assert.ThrowsExactly<KeyLoadException>(reopened.Snapshots.Recover).Code).IsEqualTo(ErrorCode.Corruption); }
            await Assert.That(reopened.Snapshots.Current).IsEqualTo(published);
            await Assert.That(reopened.Log.State.CommittedIndex).IsEqualTo((long)TailCut);
            await Assert.That(reopened.Canonical.Identity.NodeId).IsEqualTo(trial.Ready.NodeId);
            await Assert.That(reopened.Canonical.Identity.ReadGeneration).IsEqualTo((long)InstalledReadGeneration);
            await ReplicaProcessAssertions.DocumentAsync(reopened, TailCut);
            await ReplicaProcessAssertions.ReceiptAsync(reopened, TailCut);
        });

    internal static async Task ContinueAsync(ReplicaProcessTrial trial, ReplicaSnapshot published, CancellationToken token)
    {
        await ReplicaPrefixGcOwners.NodeAsync(trial.DirectoryPath, trial.Incarnation, node =>
            ReplicaPrefixGcOwners.MaterializerAsync(node, async materializer =>
            {
                await ReplicaProcessAssertions.WaitAsync(materializer, TailCut, token);
                await Assert.That(node.Snapshots.Current).IsEqualTo(published);
                await ReplicaProcessAssertions.DocumentAsync(node, TailCut);
                await ReplicaProcessAssertions.ReceiptAsync(node, TailCut);
                var operation = HealthyOperation(node);
                node.Log.Append([new(HealthyCut, NativeTerm, operation)]);
                materializer.Commit(HealthyCut);
                await ReplicaProcessAssertions.WaitAsync(materializer, HealthyCut, token);
                await HealthyAsync(node);
            }));
        await ReplicaPrefixGcOwners.NodeAsync(trial.DirectoryPath, trial.Incarnation, node =>
            ReplicaPrefixGcOwners.MaterializerAsync(node, async materializer =>
            {
                await ReplicaProcessAssertions.WaitAsync(materializer, HealthyCut, token);
                await Assert.That(node.Snapshots.Current).IsEqualTo(published);
                await HealthyAsync(node);
            }));
    }

    private static async Task HealthyAsync(ReplicaCrashNode node)
    {
        var expected = new DocumentResult(ReplicaCrashModel.Document, HealthyRevision, HealthyJson, false, []);
        var actual = node.Database.GetDocument(ReplicaCrashModel.PrincipalId, ReplicaCrashModel.Document);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(node.Database.LastApplied).IsEqualTo((long)HealthyCut);
        await Assert.That(node.Log.State.CommittedIndex).IsEqualTo((long)HealthyCut);
        await ReplicaPrefixGcReceiptAssertions.VerifyOperationAsync(node, HealthyOperation(node), HealthyCut);
        await ReplicaPrefixGcReceiptAssertions.VerifyAsync(node, TailCut);
    }

    private static ReplicatedOperation HealthyOperation(ReplicaCrashNode node)
    {
        var command = new CommandRequest(HealthyCommand, ReplicaCrashModel.Document.Partition,
            [new PutDocument(ReplicaCrashModel.Document.Collection, ReplicaCrashModel.Document.Id, HealthyJson, TailCut - NativeTerm)]);
        return node.Database.NormalizeOperation(new(HealthyCommand, OperationKind.Batch, ReplicaCrashModel.PrincipalId,
            DateTimeOffset.UnixEpoch.AddTicks(HealthyCut), Encoding.UTF8.GetString(JsonDefaults.Serialize(command))));
    }
}
