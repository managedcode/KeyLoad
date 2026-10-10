using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.UnitTests.Features.ResourceExecution;
using TUnit.Assertions.Enums;
namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class TargetInboxCancellationTests
{
    [Test]
    public async Task CancelledTargetAdmissionHasNoNativeCommitThenSameOriginalOperationAndSourceAckComplete()
    {
        using var fixture = new TestDatabase();
        var seed = TargetInboxNativeSetup.Create(fixture);
        var before = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();
        var coordinator = new EmbeddedCoordinator(fixture.Database);
        OperationResult? partial = null;
        var failure = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => partial =
            await coordinator.SubmitAsync(OperationKind.CommitInbox, seed.Request.CommandId, TargetInboxUnitProtocol.Root,
                JsonSerializer.Serialize(seed.Request, JsonDefaults.Options), caller.Token));
        await Assert.That(failure!.CancellationToken).IsEqualTo(caller.Token);
        await Assert.That(partial).IsNull();
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(before, CollectionOrdering.Matching);
        var actual = await coordinator.SubmitAsync(OperationKind.CommitInbox, seed.Request.CommandId, TargetInboxUnitProtocol.Root,
            JsonSerializer.Serialize(seed.Request, JsonDefaults.Options));
        await Assert.That(actual.Get<CommitInboxResult>().AlreadyProcessed).IsFalse();
        await TargetInboxNativeAssertions.EffectsAsync(fixture.Database, seed.Request);
        var after = QueueWholeFlowStorage.Bytes(fixture.Store);
        var replay = await coordinator.SubmitAsync(OperationKind.CommitInbox, seed.Request.CommandId, TargetInboxUnitProtocol.Root,
            JsonSerializer.Serialize(seed.Request, JsonDefaults.Options));
        await NativeReplayResultAssertions.Same<CommitInboxResult>(replay, actual);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(after, CollectionOrdering.Matching);
        var ack = new DeliveryCommand(Guid.NewGuid(), seed.Source, seed.Delivery.Token, DeliveryAction.Ack);
        _ = fixture.Submit(OperationKind.Delivery, ack, id: ack.CommandId).Get<CommitReceipt>();
        await Assert.That(fixture.Database.InspectMessage(TargetInboxUnitProtocol.Root, seed.Source,
            TargetInboxUnitProtocol.Message)!.Metadata.State).IsEqualTo(MessageState.Acked);
    }
}
