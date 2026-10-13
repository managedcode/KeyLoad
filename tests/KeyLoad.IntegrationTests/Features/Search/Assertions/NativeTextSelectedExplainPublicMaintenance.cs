using System.Security.Cryptography;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextSelectedExplainPublicMaintenance
{
    internal static async Task FirstAsync(TextIndexMaintenanceResult actual, TextIndexMaintenanceRequest request,
        ResourceDefinition resource, long sequence, NodeStatus before, NodeStatus after)
    {
        await Assert.That(actual.CommandId).IsEqualTo(request.CommandId);
        await SqlRf3Protocol.EqualAsync(request.Consumer, actual.Consumer);
        await Assert.That(actual.IndexGeneration).IsEqualTo(1L);
        await Assert.That(actual.Phase).IsEqualTo(TextIndexMaintenancePhase.Completed);
        await Assert.That(actual.TrackedRecords).IsEqualTo(2);
        await Assert.That(actual.IndexedThroughSequence).IsEqualTo(sequence);
        await Assert.That(actual.ReleasedConsumer).IsNull();
        await Assert.That(actual.IndexSha256).IsNotNull();
        await Assert.That(actual.IndexSha256!.Length).IsEqualTo(64);
        await Assert.That(actual.Checkpoint).IsNotNull();
        await Assert.That(actual.Checkpoint!.Checkpoint).IsEqualTo(sequence);
        var receipt = actual.Checkpoint.Receipt;
        await Assert.That(receipt.Mutations.IsDefault).IsFalse();
        await SqlRf3Protocol.EqualAsync(Array.Empty<MutationReceipt>(), receipt.Mutations.ToArray());
        await Assert.That(receipt.CommandId).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(request.Consumer.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(request.Placement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(request.Placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(actual.Source).IsNotNull();
        var source = actual.Source!;
        await Assert.That(source.NodeId).IsEqualTo(request.NodeId);
        await Assert.That(source.Incarnation).IsEqualTo(request.Placement.Incarnation);
        await Assert.That(source.SchemaVersion).IsEqualTo(1L);
        await Assert.That(source.PolicyEpoch).IsGreaterThan(0L);
        await Assert.That(source.ThroughSequence).IsEqualTo(sequence);
        await Assert.That(source.ResourceSha256).IsEqualTo(Convert.ToHexStringLower(
            SHA256.HashData(NativeSerialization.Serialize(resource))));
        await NativeTextMaintenanceRf3ReplayAssertions.SourceAsync(source, source, before, after);
    }
}
