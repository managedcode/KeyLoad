using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;
using ManagedCode.Communication;
using static KeyLoad.UnitTests.Features.Messaging.RemoteTransferCapacityAuthorityProtocol;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferCapacityAuthorityCeiling
{
    private const string CapacityFailure = "The retained queue transfer capacity is exhausted.";

    internal static async Task RunAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var fixture = new RemoteTransferDatabase();
        var transfer = await RemoteTransferCapacityAuthoritySeed.CreateAsync(fixture, Original);
        var accepted = await RemoteTransferCapacityAuthoritySeed.AcceptAsync(fixture, transfer);
        var request = new CommandRequest(Guid.NewGuid(), fixture.SourcePartition,
            [new CompleteQueueTransfer(fixture.SourceQueue, transfer.Mutation.TransferId, accepted.Proof.ReceiptToken)]);
        var key = RemoteTransferStorage.SourceCapacityKey(fixture.SourceQueue);
        var original = fixture.Store.Commit((tx, _) =>
        {
            var bytes = tx.ReadOwnedValue(key) ?? throw new InvalidOperationException(RuntimeMissingRow);
            var counter = OrleansSerialization.Deserialize<RemoteTransferCapacity>(bytes);
            tx.PutRecord(key, counter with
            {
                StoredBytes = long.MaxValue
            });
            return bytes;
        });
        var before = Domain(fixture, request.CommandId);
        var position = fixture.Store.Position;
        var canonical = fixture.Database.LastApplied;
        var time = TimeProvider.System.GetUtcNow();
        var failed = fixture.Apply(OperationKind.Batch, request, time: time);
        await Assert.That(Errors.Code(failed.Problem)).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(failed.Problem?.Detail).IsEqualTo(CapacityFailure);

        await Assert.That(failed.Value).IsNull();
        await Assert.That(fixture.Store.Position).IsEqualTo(position + First);
        await Assert.That(fixture.Database.LastApplied).IsEqualTo(canonical + First);
        await Assert.That(Domain(fixture, request.CommandId)).IsEquivalentTo(before, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await RequireLocatorAsync(fixture, request.CommandId);
        await Assert.That(QueueWholeFlowStorage.Clock(RemoteTransferCapacityAuthorityStorage.Execution(fixture))).IsEqualTo(time);
        var failureKey = KeySpace.PartitionOutcome(fixture.SourcePartition, RemoteTransferDatabase.RootPrincipal, request.CommandId);
        var retained = fixture.Store.Read(view => view.ReadOwnedValue(failureKey)) ?? throw new InvalidOperationException(RuntimeMissingRow);
        await FailureReplayAsync(fixture, request, failed, retained);
        var coldImage = RemoteTransferCapacityAuthorityStorage.Capture(fixture);
        var coldPosition = fixture.Store.Position;
        var identity = fixture.Store.Identity with
        {
            SigningKey = fixture.Store.Identity.SigningKey.ToArray()
        };
        fixture.Reopen();
        await RemoteTransferCapacityAuthorityStorage.IdentityAsync(fixture, identity, coldPosition);
        await RemoteTransferCapacityAuthorityStorage.ImageAsync(fixture, coldImage, coldPosition);
        await FailureReplayAsync(fixture, request, failed, retained);
        fixture.Store.Commit((tx, _) => { tx.Put(key, original); return true; });
        await FailureReplayAsync(fixture, request, failed, retained);
        var completed = await RemoteTransferCapacityAuthoritySettlement.CompleteAcceptedAsync(fixture, transfer, First, accepted);
        var healthy = await RemoteTransferCapacityAuthoritySeed.CreateAsync(fixture, Healthy);
        var second = await RemoteTransferCapacityAuthoritySettlement.FinishAsync(fixture, healthy, SecondValue);
        await RemoteTransferCapacityAuthorityCold.VerifyAsync(fixture, [completed, second]);
        await FailureReplayAsync(fixture, request, failed, retained);
    }

    private static async Task FailureReplayAsync(RemoteTransferDatabase fixture, CommandRequest request, Result<object> original, byte[] retained)
    {
        var image = RemoteTransferCapacityAuthorityStorage.Capture(fixture);
        var position = fixture.Store.Position;
        var stored = OutcomeStoreOracle.ReadPartition(RemoteTransferCapacityAuthorityStorage.Execution(fixture), request.Partition,
            RemoteTransferDatabase.RootPrincipal, request.CommandId);
        await Assert.That(stored).IsEqualTo(original);
        await Assert.That(fixture.Apply(OperationKind.Batch, request)).IsEqualTo(original);
        var key = KeySpace.PartitionOutcome(fixture.SourcePartition, RemoteTransferDatabase.RootPrincipal, request.CommandId);
        var actual = fixture.Store.Read(view => view.ReadOwnedValue(key));
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.AsSpan().SequenceEqual(retained)).IsTrue();
        await RemoteTransferCapacityAuthorityStorage.ImageAsync(fixture, image, position);
    }

    private static string[] Domain(RemoteTransferDatabase fixture, Guid id)
    {
        var outcome = Convert.ToHexString(KeySpace.PartitionOutcome(fixture.SourcePartition, RemoteTransferDatabase.RootPrincipal, id)) + ":";
        var locator = Convert.ToHexString(KeySpace.OutcomeLocator(fixture.SourcePartition, RemoteTransferDatabase.RootPrincipal, id)) + ":";
        var clock = Convert.ToHexString(KeySpace.ClockBytes) + ":";
        var applied = Convert.ToHexString(KeySpace.AppliedBytes) + ":";
        return RemoteTransferCapacityAuthorityStorage.Capture(fixture).Where(row => !row.StartsWith(outcome, StringComparison.Ordinal)
            && !row.StartsWith(locator, StringComparison.Ordinal) && !row.StartsWith(clock, StringComparison.Ordinal)
            && !row.StartsWith(applied, StringComparison.Ordinal)).ToArray();
    }

    private static async Task RequireLocatorAsync(RemoteTransferDatabase fixture, Guid id)
    {
        var key = KeySpace.OutcomeLocator(fixture.SourcePartition, RemoteTransferDatabase.RootPrincipal, id);
        var value = fixture.Store.Read(view => view.ReadOwnedValue(key));
        var expected = KeySpace.PartitionOutcome(fixture.SourcePartition, RemoteTransferDatabase.RootPrincipal, id);
        await Assert.That(value).IsNotNull();
        await Assert.That(value!.AsSpan().SequenceEqual(expected)).IsTrue();
    }
}
