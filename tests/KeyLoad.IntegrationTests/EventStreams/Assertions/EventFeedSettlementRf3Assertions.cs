namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedSettlementRf3Assertions
{
    internal static async Task ResultAsync(EventFeedControlRequest request, EventFeedControlResult actual,
        long revision, bool released)
    {
        await Assert.That(actual.Version).IsEqualTo(EventFeedSettlementProtocol.Version);
        await Assert.That(actual.PublicCommandId).IsEqualTo(request.CommandId);
        await Assert.That(actual.MapId).IsEqualTo(request.MapId);
        await Assert.That(actual.Revision).IsEqualTo(revision);
        await Assert.That(actual.CoverageGeneration).IsEqualTo(request.ExpectedCoverageGeneration);
        await Assert.That(actual.Released).IsEqualTo(released);
        await Assert.That(actual.Retained).IsEqualTo(!released);
        await Assert.That(actual.Cursor is null).IsEqualTo(released);
        await Assert.That(actual.OfferDigest).IsNull();
        await Assert.That(actual.Pages.IsEmpty).IsTrue();
        await Assert.That(actual.Receipt.CommandId).IsNotEqualTo(request.CommandId);
        await Assert.That(actual.Receipt.Token.AtomicPartitionId).IsEqualTo(request.ControlPartition.AtomicPartitionId);
        await Assert.That(actual.Receipt.Token.Position).IsGreaterThan(0);
        await Assert.That(actual.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(actual.Receipt.Mutations.IsDefault).IsFalse();
    }
}
