using KeyLoad.Replication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal sealed class NodeEpochRf3Migration(string priorRoot, string currentRoot,
    NodeEpochRf3Profile profile, NodeEpochRf3NodeObservation[] originalNodes)
{
    private const string PrepareOperation = "prepare-native-node";
    private const string VerifyOperation = "verify-native-node";
    private const string PublishOperation = "publish-native-node";
    private const string InvalidThirdDirectory = "invalid-third";

    private NodeEpochRf3Inventory[] sourceInventories = [];
    private ServerNodeUpgradeReceipt[] receipts = [];
    internal ReplicaSnapshot PriorSnapshot(string nodeName)
    {
        if (receipts.Length != NodeEpochRf3Protocol.NodeCount)
        { throw new InvalidOperationException("Prior snapshot receipts were not validated."); }
        return receipts[NodeEpochRf3MigrationAssertions.NodeIndex(nodeName)].OriginalReplicaHardState.Snapshot
            ?? throw new InvalidOperationException("The genuine prior node has no published snapshot pointer.");
    }

    internal async Task<NodeEpochRf3Inventory[]> CaptureSourcesAsync(CancellationToken cancellationToken)
    {
        sourceInventories = new NodeEpochRf3Inventory[NodeEpochRf3Protocol.NodeCount];
        for (var index = 0; index < NodeEpochRf3Protocol.NodeCount; index++)
        {
            var nodeRoot = Path.Combine(priorRoot, NodeName(index));
            sourceInventories[index] = await NodeEpochRf3Inventory.CaptureAsync(nodeRoot, cancellationToken)
                .ConfigureAwait(false);
        }
        return sourceInventories;
    }

    internal async Task VerifyOriginalProfileAsync(byte[] profileBytes, string expectedSha,
        CancellationToken cancellationToken)
    {
        var priorProfile = await NodeEpochRf3Profile.ReadAsync(Path.Combine(priorRoot,
            NodeEpochRf3Protocol.ProfileFile), cancellationToken).ConfigureAwait(false);
        var currentProfile = await NodeEpochRf3Profile.ReadAsync(Path.Combine(currentRoot,
            NodeEpochRf3Protocol.ProfileFile), cancellationToken).ConfigureAwait(false);
        await Assert.That(priorProfile.Bytes.AsSpan().SequenceEqual(profileBytes)).IsTrue();
        await Assert.That(currentProfile.Bytes.AsSpan().SequenceEqual(profileBytes)).IsTrue();
        await Assert.That(priorProfile.Sha256).IsEqualTo(expectedSha);
        await Assert.That(currentProfile.Sha256).IsEqualTo(expectedSha);
        await Assert.That(priorProfile.Profile.Incarnation == profile.Incarnation
            && currentProfile.Profile.Incarnation == profile.Incarnation).IsTrue();
    }

    internal async Task VerifyInvalidThirdBarrierAsync(string negativeRoot, CancellationToken cancellationToken)
    {
        var privateRoot = Path.Combine(negativeRoot, InvalidThirdDirectory);
        var sourceRoot = Path.Combine(privateRoot, "source");
        var targetRoot = Path.Combine(privateRoot, "targets");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(targetRoot);
        var privateThird = Path.Combine(sourceRoot, NodeEpochRf3Protocol.Node3);
        var copy = await NodeEpochRf3Inventory.CopyTreeAsync(Path.Combine(priorRoot, NodeEpochRf3Protocol.Node3),
            privateThird, cancellationToken).ConfigureAwait(false);
        await NodeEpochRf3Inventory.WriteOwnedUnknownAsync(privateThird, cancellationToken).ConfigureAwait(false);
        var destinations = new string[NodeEpochRf3Protocol.NodeCount];
        for (var index = 0; index < NodeEpochRf3Protocol.NodeCount; index++)
        {
            var nodeName = NodeName(index);
            var source = index == 2 ? privateThird : Path.Combine(priorRoot, nodeName);
            destinations[index] = Path.Combine(targetRoot, nodeName);
            var result = await NodeEpochRf3OfflineUpgrade.RunAsync(PrepareOperation, source,
                destinations[index], profile, nodeName, cancellationToken).ConfigureAwait(false);
            if (index < 2)
            { await Assert.That(result.ExitCode).IsEqualTo(0); }
            else
            {
                await Assert.That(result.ExitCode).IsEqualTo(1);
                await Assert.That(result.Stderr).IsEqualTo("FormatUnsupported\n");
            }
            await Assert.That(Directory.Exists(destinations[index])).IsFalse();
        }
        var privateAfter = await NodeEpochRf3Inventory.CaptureAsync(privateThird, cancellationToken).ConfigureAwait(false);
        await Assert.That(privateAfter.Files.Length).IsEqualTo(copy.Files.Length + 1);
        await NodeEpochRf3MigrationAssertions.AssertOriginalInventoriesAsync(priorRoot, sourceInventories,
            cancellationToken).ConfigureAwait(false);
        await NodeEpochRf3Inventory.DeleteOwnedUnknownAsync(privateThird, cancellationToken).ConfigureAwait(false);
        var restoredCopy = await NodeEpochRf3Inventory.CaptureAsync(privateThird, cancellationToken).ConfigureAwait(false);
        await Assert.That(copy.Equivalent(restoredCopy)).IsTrue();
    }

    internal async Task PrepareAndVerifyAllAsync(CancellationToken cancellationToken)
    {
        receipts = new ServerNodeUpgradeReceipt[NodeEpochRf3Protocol.NodeCount];
        for (var index = 0; index < NodeEpochRf3Protocol.NodeCount; index++)
        {
            var nodeName = NodeName(index);
            var source = Path.Combine(priorRoot, nodeName);
            var destination = Target(nodeName);
            var prepared = await NodeEpochRf3OfflineUpgrade.RunAsync(PrepareOperation, source, destination,
                profile, nodeName, cancellationToken).ConfigureAwait(false);
            await Assert.That(prepared.ExitCode).IsEqualTo(0);
            var verified = await NodeEpochRf3OfflineUpgrade.RunAsync(VerifyOperation, source, destination,
                profile, nodeName, cancellationToken).ConfigureAwait(false);
            await Assert.That(verified.ExitCode).IsEqualTo(0);
            var receipt = NodeEpochRf3ReceiptReader.Read(Stage(nodeName));
            await NodeEpochRf3MigrationAssertions.VerifyReceiptAsync(receipt, nodeName, profile,
                originalNodes[index].Status, sourceInventories).ConfigureAwait(false);
            receipts[index] = receipt;
        }
        await NodeEpochRf3MigrationAssertions.AssertOriginalInventoriesAsync(priorRoot, sourceInventories,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task PublishAllWithBarrierAsync(CancellationToken cancellationToken)
    {
        await PublishAndVerifyAsync(0, cancellationToken).ConfigureAwait(false);
        var stageTwo = Stage(NodeEpochRf3Protocol.Node2);
        await NodeEpochRf3Inventory.WriteOwnedUnknownAsync(stageTwo, cancellationToken).ConfigureAwait(false);
        var blocked = await NodeEpochRf3OfflineUpgrade.RunAsync(PublishOperation,
            Path.Combine(priorRoot, NodeEpochRf3Protocol.Node2), Target(NodeEpochRf3Protocol.Node2),
            profile, NodeEpochRf3Protocol.Node2, cancellationToken).ConfigureAwait(false);
        await Assert.That(blocked.ExitCode).IsEqualTo(1);
        await Assert.That(blocked.Stderr).IsEqualTo("FormatUnsupported\n");
        await Assert.That(Directory.Exists(Target(NodeEpochRf3Protocol.Node2))).IsFalse();
        await Assert.That(Directory.Exists(Target(NodeEpochRf3Protocol.Node3))).IsFalse();
        await NodeEpochRf3MigrationAssertions.AssertOriginalInventoriesAsync(priorRoot, sourceInventories,
            cancellationToken).ConfigureAwait(false);
        await NodeEpochRf3Inventory.DeleteOwnedUnknownAsync(stageTwo, cancellationToken).ConfigureAwait(false);
        await PublishAndVerifyAsync(1, cancellationToken).ConfigureAwait(false);
        await PublishAndVerifyAsync(2, cancellationToken).ConfigureAwait(false);
        await VerifyPublishedAllAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task VerifyPublishedRetryPreservesLaterWritesAsync(CancellationToken cancellationToken)
    {
        var before = new NodeEpochRf3Inventory[NodeEpochRf3Protocol.NodeCount];
        for (var index = 0; index < before.Length; index++)
        { before[index] = await NodeEpochRf3Inventory.CaptureAsync(Target(NodeName(index)), cancellationToken).ConfigureAwait(false); }
        for (var index = 0; index < before.Length; index++)
        {
            var nodeName = NodeName(index);
            var retry = await NodeEpochRf3OfflineUpgrade.RunAsync(PrepareOperation,
                Path.Combine(priorRoot, nodeName), Target(nodeName), profile, nodeName, cancellationToken)
                .ConfigureAwait(false);
            await Assert.That(retry.ExitCode).IsEqualTo(0);
            var verified = await NodeEpochRf3OfflineUpgrade.RunAsync(VerifyOperation,
                Path.Combine(priorRoot, nodeName), Target(nodeName), profile, nodeName, cancellationToken)
                .ConfigureAwait(false);
            await Assert.That(verified.ExitCode).IsEqualTo(0);
        }
        await NodeEpochRf3MigrationAssertions.AssertOriginalInventoriesAsync(priorRoot, sourceInventories,
            cancellationToken).ConfigureAwait(false);
        for (var index = 0; index < before.Length; index++)
        {
            var after = await NodeEpochRf3Inventory.CaptureAsync(Target(NodeName(index)), cancellationToken).ConfigureAwait(false);
            await Assert.That(before[index].Equivalent(after)).IsTrue();
        }
    }

    private async Task PublishAndVerifyAsync(int index, CancellationToken cancellationToken)
    {
        var nodeName = NodeName(index);
        var source = Path.Combine(priorRoot, nodeName);
        var destination = Target(nodeName);
        var result = await NodeEpochRf3OfflineUpgrade.RunAsync(PublishOperation, source, destination,
            profile, nodeName, cancellationToken).ConfigureAwait(false);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        var verified = await NodeEpochRf3OfflineUpgrade.RunAsync(VerifyOperation, source, destination,
            profile, nodeName, cancellationToken).ConfigureAwait(false);
        await Assert.That(verified.ExitCode).IsEqualTo(0);
    }

    private async Task VerifyPublishedAllAsync(CancellationToken cancellationToken)
    {
        for (var index = 0; index < NodeEpochRf3Protocol.NodeCount; index++)
        {
            var nodeName = NodeName(index);
            var verified = await NodeEpochRf3OfflineUpgrade.RunAsync(VerifyOperation,
                Path.Combine(priorRoot, nodeName), Target(nodeName), profile, nodeName, cancellationToken)
                .ConfigureAwait(false);
            await Assert.That(verified.ExitCode).IsEqualTo(0);
            var receipt = NodeEpochRf3ReceiptReader.Read(Target(nodeName));
            await Assert.That(receipt).IsEqualTo(receipts[index]);
        }
    }

    private string Target(string nodeName) => Path.Combine(currentRoot, nodeName);
    private string Stage(string nodeName) => Target(nodeName) + NodeEpochRf3Protocol.StageSuffix;

    private static string NodeName(int index) => index switch
    {
        0 => NodeEpochRf3Protocol.Node1,
        1 => NodeEpochRf3Protocol.Node2,
        2 => NodeEpochRf3Protocol.Node3,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}
