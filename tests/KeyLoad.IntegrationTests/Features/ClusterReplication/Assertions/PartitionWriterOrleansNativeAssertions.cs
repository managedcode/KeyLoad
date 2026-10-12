using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Replication;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class PartitionWriterOrleansNativeAssertions
{
    internal static async Task OriginalAsync(PartitionMovementPublicParentRf3Seed seed, ReplicaEntry entry,
        PartitionMovePhaseCommand command, OperationResult result)
    {
        var operation = entry.Operation!;
        var body = NativeSerialization.Deserialize<PartitionMoveCompletionBody>(command.Body.Span);
        var actual = result.Get<PartitionMovePhaseResult>();
        var expectedIdentity = PartitionMovementParentPhaseIds.For(seed.FirstRequest,
            PartitionMovementPublicParentRf3Administrator.PrincipalId, PartitionMovementParentPhaseRole.CompleteRetirement);
        await Assert.That(operation.Id).IsEqualTo(expectedIdentity);
        await Assert.That(operation.Kind).IsEqualTo(OperationKind.PartitionMovementPhase);
        await Assert.That(command.MoveId).IsEqualTo(seed.FirstRequest.MoveId);
        await SqlRf3Protocol.EqualAsync(seed.Partition, command.Partition);
        await SqlRf3Protocol.EqualAsync(seed.Directory.ControlOwner, command.ControlOwner);
        await Assert.That(command.Stage).IsEqualTo(PartitionMovePeerStage.ControlCompleteRetirement);
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.SafeDetail).IsNull();
        await Assert.That(result.Json).IsNull();
        await Assert.That(actual.MoveId).IsEqualTo(command.MoveId);
        await Assert.That(actual.Stage).IsEqualTo(command.Stage);
        await Assert.That(actual.Journal.CommandId).IsEqualTo(operation.Id);
        await Assert.That(actual.Journal.AppliedPosition).IsEqualTo(entry.Index);
        await Assert.That(actual.Journal.ControlIntentDigest).IsEqualTo(command.ControlIntentDigest);
        await SqlRf3Protocol.EqualAsync(command.ControlOwner, actual.Journal.PhysicalOwner);
        await SqlRf3Protocol.EqualAsync(body.Control with { Phase = PartitionMovePhase.Retired }, actual.Control);
        await SqlRf3Protocol.EqualAsync(body.Control.PublishedPlacement, actual.PublishedPlacement);
        await Assert.That(actual.Fence).IsNull();
        await Assert.That(actual.InstalledReceipt).IsNull();
        await Assert.That(actual.Journal.EffectDigest).IsNull();
        await Assert.That(actual.Grant).IsNull();
        await Assert.That(actual.Cleanup).IsNull();
        await Assert.That(actual.ControlledCommand).IsNull();
        await Assert.That(actual.ControlledEffect).IsNull();
        await Assert.That(actual.ReceiverIssuance).IsNull();
        await Assert.That(actual.RetireCancellation).IsNull();
    }

    internal static async Task ImagesAsync(PartitionWriterOrleansImage[] before,
        PartitionWriterOrleansImage[] after, string? appendedNode = null)
    {
        await Assert.That(after.Length).IsEqualTo(TwoRf3MembershipProtocol.NodeCount);
        foreach (var expected in before)
        {
            var actual = after.Single(image => image.Node == expected.Node);
            var expectedPosition = appendedNode == expected.Node
                ? checked(expected.ReplicaPosition + PartitionWriterOrleansProtocol.PositionStep) : expected.ReplicaPosition;
            await SqlRf3Protocol.EqualAsync(expected with { ReplicaPosition = expectedPosition }, actual);
        }
    }
}
