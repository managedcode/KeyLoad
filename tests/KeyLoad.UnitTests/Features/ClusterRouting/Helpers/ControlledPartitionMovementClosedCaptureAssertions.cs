using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Exercises the actual old capture after its durable marker and requires no allocation or canonical effects.</summary>
internal static class ControlledPartitionMovementClosedCaptureAssertions
{
    internal static async Task AssertAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementCaptureRuntime actualOwner, PartitionMovementTransportRequest originalVerified,
        CancellationToken cancellationToken)
    {
        var before = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var position = source.Store.Position;
        var index = source.Journal.Log.State.LastIndex;
        var memory = actualOwner.Memory;
        var principal = ControlledPartitionMovementCaptureFlow.Principal(source, originalVerified);
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            actualOwner.Source.CaptureAsync(principal, originalVerified.Envelope, cancellationToken))
            ?? throw new InvalidOperationException("The genuine closed capture rejection is absent.");
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(failure.Message).IsEqualTo(PartitionMoveProtocol.Fenced);
        await Assert.That(JsonDefaults.Serialize(actualOwner.Memory)
            .SequenceEqual(JsonDefaults.Serialize(memory))).IsTrue();
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(index);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store)
            .SequenceEqual(before, StringComparer.Ordinal)).IsTrue();
    }
}
