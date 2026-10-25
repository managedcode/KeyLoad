using KeyLoad.FunctionalTests.ResourceExecution;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;
namespace KeyLoad.FunctionalTests.Messaging;

internal sealed class TargetInboxAtomicProcessingTests
{
    [Test]
    public async Task TargetEffectsInboxAndQuotaSurviveTwoColdCutsWhileSourceAckRemainsSeparateAndFreshlyFenced()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var fixture = new TestDatabase();
        var seed = TargetInboxSetup.Create(fixture);
        var failed = seed.Request with
        {
            CommandId = Guid.NewGuid(),
            Effects =
            [new PutDocument(TargetInboxUnitProtocol.Collection, TargetInboxUnitProtocol.Document,
                TargetInboxUnitProtocol.Payload, TargetInboxUnitProtocol.BadRevision)]
        };
        await TargetInboxAssertions.RefusedAsync(fixture.Database, fixture.Store, failed, ErrorCode.RevisionConflict, token);
        var originalResult = TargetInboxSetup.Apply(fixture.Database, seed.Request, token);
        var original = originalResult.Get<CommitInboxResult>();
        await Assert.That(original.AlreadyProcessed).IsFalse();
        await Assert.That(fixture.Database.InspectMessage(TargetInboxUnitProtocol.Root, seed.Source,
            TargetInboxUnitProtocol.Message)!.Metadata.State).IsEqualTo(MessageState.Leased);
        await TargetInboxAssertions.EffectsAsync(fixture.Database, seed.Request);
        await TargetInboxAssertions.CapacityAsync(fixture.Store, seed.Request, TargetInboxUnitProtocol.FirstRevision);
        var image = TargetInboxAssertions.Image(fixture.Store, seed.Source.Partition, seed.Target.Partition);
        fixture.Store.Dispose();
        using var first = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(first);
        await ReplayResultAssertions.Same<CommitInboxResult>(TargetInboxSetup.Apply(database, seed.Request, token), originalResult);
        await Assert.That(TargetInboxAssertions.Image(first, seed.Source.Partition, seed.Target.Partition)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await TargetInboxPermissions.ReplayAsync(database, first, seed.Request, original, token);
        await TargetInboxAssertions.RefusedAsync(database, first, seed.Request with
        {
            CommandId = Guid.NewGuid(),
            Effects =
            [new PutDocument(TargetInboxUnitProtocol.Collection, TargetInboxUnitProtocol.Changed, TargetInboxUnitProtocol.Payload)]
        }, ErrorCode.Conflict, token);
        var ack = new DeliveryCommand(Guid.NewGuid(), seed.Source, seed.Delivery.Token, DeliveryAction.Ack);
        var ackResult = database.ApplyEmbedded(database.CreateCommand(OperationKind.Delivery, ack.CommandId, TargetInboxUnitProtocol.Root, default, System.Text.Json.JsonSerializer.Serialize(ack, JsonDefaults.Options)), token);
        _ = ackResult.Get<CommitReceipt>();
        await Assert.That(database.InspectMessage(TargetInboxUnitProtocol.Root, seed.Source,
            TargetInboxUnitProtocol.Message)!.Metadata.State).IsEqualTo(MessageState.Acked);
        image = TargetInboxAssertions.Image(first, seed.Source.Partition, seed.Target.Partition);
        first.Dispose();
        using var second = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var cold = QueueWholeFlowStorage.Open(second);
        await ReplayResultAssertions.Same<CommitInboxResult>(TargetInboxSetup.Apply(cold, seed.Request, token), originalResult);
        await Assert.That(TargetInboxAssertions.Image(second, seed.Source.Partition, seed.Target.Partition)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await TargetInboxQuota.ContinueAsync(cold, second, seed.Request, token);
    }
}
