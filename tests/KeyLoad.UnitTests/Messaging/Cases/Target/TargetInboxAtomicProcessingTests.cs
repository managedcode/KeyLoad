using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using TUnit.Assertions.Enums;
namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class TargetInboxAtomicProcessingTests
{
    [Test]
    public async Task TargetEffectsInboxAndQuotaSurviveTwoColdCutsWhileSourceAckRemainsSeparateAndFreshlyFenced()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        using var fixture = new TestDatabase();
        var seed = TargetInboxNativeSetup.Create(fixture);
        var failed = seed.Request with
        {
            CommandId = Guid.NewGuid(),
            Effects =
            [new PutDocument(TargetInboxUnitProtocol.Collection, TargetInboxUnitProtocol.Document,
                TargetInboxUnitProtocol.Payload, TargetInboxUnitProtocol.BadRevision)]
        };
        await TargetInboxNativeAssertions.RefusedAsync(fixture.Database, fixture.Store, failed, ErrorCode.RevisionConflict, token);
        var originalResult = TargetInboxNativeSetup.Apply(fixture.Database, seed.Request, token);
        var original = originalResult.Get<CommitInboxResult>();
        await Assert.That(original.AlreadyProcessed).IsFalse();
        await Assert.That(fixture.Database.InspectMessage(TargetInboxUnitProtocol.Root, seed.Source,
            TargetInboxUnitProtocol.Message)!.Metadata.State).IsEqualTo(MessageState.Leased);
        await TargetInboxNativeAssertions.EffectsAsync(fixture.Database, seed.Request);
        await TargetInboxNativeAssertions.CapacityAsync(fixture.Store, seed.Request, TargetInboxUnitProtocol.FirstRevision);
        var image = TargetInboxNativeAssertions.Image(fixture.Store, seed.Source.Partition, seed.Target.Partition);
        fixture.Store.Dispose();
        using var first = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(first);
        await NativeReplayResultAssertions.Same<CommitInboxResult>(TargetInboxNativeSetup.Apply(database, seed.Request, token), originalResult);
        await Assert.That(TargetInboxNativeAssertions.Image(first, seed.Source.Partition, seed.Target.Partition)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await TargetInboxNativePermissions.ReplayAsync(database, first, seed.Request, original, token);
        await TargetInboxNativeAssertions.RefusedAsync(database, first, seed.Request with
        {
            CommandId = Guid.NewGuid(),
            Effects =
            [new PutDocument(TargetInboxUnitProtocol.Collection, TargetInboxUnitProtocol.Changed, TargetInboxUnitProtocol.Payload)]
        }, ErrorCode.Conflict, token);
        var ack = new DeliveryCommand(Guid.NewGuid(), seed.Source, seed.Delivery.Token, DeliveryAction.Ack);
        var ackResult = database.ApplyEmbedded(new(ack.CommandId, OperationKind.Delivery, TargetInboxUnitProtocol.Root, default,
            System.Text.Json.JsonSerializer.Serialize(ack, JsonDefaults.Options)), token);
        _ = ackResult.Get<CommitReceipt>();
        await Assert.That(database.InspectMessage(TargetInboxUnitProtocol.Root, seed.Source,
            TargetInboxUnitProtocol.Message)!.Metadata.State).IsEqualTo(MessageState.Acked);
        image = TargetInboxNativeAssertions.Image(first, seed.Source.Partition, seed.Target.Partition);
        first.Dispose();
        using var second = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var cold = QueueWholeFlowStorage.Open(second);
        await NativeReplayResultAssertions.Same<CommitInboxResult>(TargetInboxNativeSetup.Apply(cold, seed.Request, token), originalResult);
        await Assert.That(TargetInboxNativeAssertions.Image(second, seed.Source.Partition, seed.Target.Partition)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await TargetInboxNativeQuota.ContinueAsync(cold, second, seed.Request, token);
    }
}
