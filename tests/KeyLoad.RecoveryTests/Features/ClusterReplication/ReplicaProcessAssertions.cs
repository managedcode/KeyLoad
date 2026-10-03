using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Replication;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests;

internal static class ReplicaProcessAssertions
{
    private static readonly TimeSpan ApplyTimeout = TimeSpan.FromSeconds(15);

    internal static async Task WaitAsync(ReplicaMaterializer materializer, long index, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(ApplyTimeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        await materializer.WaitForApplyAsync(index, linked.Token);
    }

    internal static async Task DocumentAsync(ReplicaCrashNode node, long cut)
    {
        await Assert.That(node.Database.LastApplied).IsEqualTo(cut);
        var document = node.Database.GetDocument(ReplicaCrashModel.PrincipalId, ReplicaCrashModel.Document);
        await Assert.That(document).IsNotNull();
        await Assert.That(document!.Revision).IsEqualTo(cut - 1);
        await Assert.That(document.Json).IsEqualTo(ReplicaCrashModel.JsonAt(cut));
    }

    internal static async Task ReceiptAsync(ReplicaCrashNode node, int cut)
    {
        var operation = ReplicaCrashModel.Operation(cut);
        var previous = node.Database.Outcome(ReplicaCrashModel.PrincipalId, operation.Id)!.Get<CommitReceipt>();
        var replayed = node.Database.Apply(operation).Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(replayed)).IsEquivalentTo(JsonDefaults.Serialize(previous), CollectionOrdering.Matching);
        await Assert.That(replayed.CommandId).IsEqualTo(operation.Id);
        await Assert.That(replayed.Token.Position).IsEqualTo(cut);
        await Assert.That(replayed.Token.Incarnation).IsEqualTo(node.Configuration.Incarnation);
        await Assert.That(replayed.Mutations.Single().Revision).IsEqualTo(cut - 1);
    }

    internal static async Task OperationAsync(DatabaseEngine database, ReplicatedOperation? actual, ReplicatedOperation expected)
    {
        expected = database.NormalizeOperation(expected);
        await Assert.That(actual).IsNotNull();
        var observed = actual!;
        await Assert.That(database.NativeOperationsEqual(observed, expected)).IsTrue();
        await Assert.That(observed.Id).IsEqualTo(expected.Id);
        await Assert.That(observed.Kind).IsEqualTo(expected.Kind);
        await Assert.That(observed.PrincipalId).IsEqualTo(expected.PrincipalId);
        await Assert.That(observed.EvaluatedAt).IsEqualTo(expected.EvaluatedAt);
        await Assert.That(observed.PayloadJson).IsEqualTo(expected.PayloadJson);
    }

    internal static async Task SnapshotAsync(ReplicaCrashNode node, ReplicaProcessTrial trial, long applied)
    {
        var image = node.Snapshots.Current!;
        await Assert.That(image.Index).IsEqualTo(4);
        await Assert.That(image.Incarnation).IsEqualTo(trial.Incarnation);
        await Assert.That(node.Log.State.LastIndex).IsEqualTo(5);
        await Assert.That(node.Canonical.Identity.NodeId).IsEqualTo(trial.Ready.NodeId);
        await Assert.That(node.Canonical.Identity.ReadGeneration).IsEqualTo(1);
        await Assert.That(node.HasObsoleteRecord()).IsFalse();
        await Assert.That(File.Exists(ImagePath(node, image))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(node.Configuration.Directory, ReplicaProtocol.SnapshotDirectory, ReplicaProtocol.IncomingImage))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(node.Configuration.Directory, ReplicaProtocol.SnapshotDirectory, ReplicaProtocol.IncomingManifest))).IsFalse();
        var checkpoint = node.Canonical.VerifySnapshot(ImagePath(node, image));
        await Assert.That(checkpoint.AppliedPosition).IsEqualTo(4);
        if (applied == 4)
        {
            await Assert.That(node.Canonical.Position).IsEqualTo(checkpoint.Position);
        }
        await DocumentAsync(node, applied);
    }

    internal static string ImagePath(ReplicaCrashNode node, ReplicaSnapshot image)
        => Path.Combine(node.Configuration.Directory, ReplicaProtocol.SnapshotDirectory, image.FileName);
}
