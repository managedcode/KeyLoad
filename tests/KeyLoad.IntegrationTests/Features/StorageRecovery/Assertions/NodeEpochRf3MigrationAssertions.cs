using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NodeEpochRf3MigrationAssertions
{
    internal static async Task AssertOriginalInventoriesAsync(string priorRoot,
        NodeEpochRf3Inventory[] inventories, CancellationToken cancellationToken)
    {
        if (inventories.Length != NodeEpochRf3Protocol.NodeCount)
        { throw new InvalidOperationException("Prior inventories were not captured before conversion."); }
        for (var index = 0; index < inventories.Length; index++)
        {
            var actual = await NodeEpochRf3Inventory.CaptureAsync(Path.Combine(priorRoot, NodeName(index)),
                cancellationToken).ConfigureAwait(false);
            await Assert.That(inventories[index].Equivalent(actual)).IsTrue();
        }
    }

    private const int GoldenReceiptFormatVersion = 2;
    private const int Native5SourceEpoch = 5;
    private const int Native6SourceEpoch = 6;
    private const int Native7TargetEpoch = 7;

    internal static async Task VerifyReceiptAsync(ServerNodeUpgradeReceipt receipt, int expectedSourceEpoch,
        string nodeName, NodeEpochRf3Profile profile, NodeStatus status, NodeEpochRf3Inventory[] inventories)
    {
        if (expectedSourceEpoch is not (Native5SourceEpoch or Native6SourceEpoch))
        { throw new ArgumentOutOfRangeException(nameof(expectedSourceEpoch)); }
        await Assert.That(receipt.FormatVersion).IsEqualTo(GoldenReceiptFormatVersion);
        await Assert.That(receipt.SourceEpoch).IsEqualTo(expectedSourceEpoch);
        await Assert.That(receipt.TargetEpoch).IsEqualTo(Native7TargetEpoch);
        await Assert.That(receipt.Incarnation).IsEqualTo(profile.Incarnation);
        await Assert.That(receipt.CanonicalNodeId).IsEqualTo(Guid.Parse(status.NodeId));
        await Assert.That(receipt.CanonicalAppliedPosition).IsGreaterThanOrEqualTo(status.Applied);
        await Assert.That(receipt.CanonicalPosition).IsGreaterThanOrEqualTo(receipt.CanonicalAppliedPosition);
        await Assert.That(receipt.OriginalReplicaHardState.Snapshot).IsNotNull();
        var snapshot = receipt.OriginalReplicaHardState.Snapshot!;
        await Assert.That(snapshot.Incarnation).IsEqualTo(profile.Incarnation);
        await Assert.That(snapshot.Index).IsGreaterThanOrEqualTo(NodeEpochRf3Protocol.SnapshotThreshold);
        await Assert.That(snapshot.Index).IsLessThanOrEqualTo(receipt.OriginalReplicaHardState.CommittedIndex);
        await Assert.That(receipt.CanonicalAppliedPosition).IsGreaterThanOrEqualTo(snapshot.Index);
        await Assert.That(snapshot.FileName.Length).IsGreaterThan(0);
        var imagePath = Path.Combine("snapshots", snapshot.FileName);
        var image = inventories[NodeIndex(nodeName)].Files.SingleOrDefault(file => file.Path == imagePath);
        await Assert.That(image).IsNotNull();
        await Assert.That(image!.Length).IsEqualTo(snapshot.Length);
        await Assert.That(image.Sha256).IsEqualTo(snapshot.Sha256);
    }

    internal static int NodeIndex(string nodeName) => nodeName switch
    {
        NodeEpochRf3Protocol.Node1 => 0,
        NodeEpochRf3Protocol.Node2 => 1,
        NodeEpochRf3Protocol.Node3 => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(nodeName))
    };

    private static string NodeName(int index) => index switch
    {
        0 => NodeEpochRf3Protocol.Node1,
        1 => NodeEpochRf3Protocol.Node2,
        2 => NodeEpochRf3Protocol.Node3,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}
