using KeyLoad.Core;
using ManagedCode.Communication.Commands;
using KeyLoad.UnitTests.Features.Messaging;
using P = KeyLoad.UnitTests.Features.TimeSeries.SampleChunkCanonicalFixture;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleSequenceIntegrityRecovery
{
    private const string Missing = "The failed original native sequence operation is absent.";
    private const string MissingOutcome = "The applied command has no durable outcome.";

    internal static async Task RunAsync(SampleSequenceIntegrityFixture fixture, byte[] counter, long persistedSequence,
        CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), fixture.Source.Owner.Partition,
            [SampleSequenceIntegrityFlow.Append()]);
        var issued = fixture.Database.CreateCommand(OperationKind.Batch, command.CommandId, P.Principal,
            TimeProvider.System.GetUtcNow(), NativeSerialization.Serialize(command));
        var image = SampleChunkCorrectionBoundColdAssertions.Image(fixture.Store);
        var position = fixture.Store.Position;
        var identity = KeyLoad.UnitTests.Features.StorageRecovery.Assertions.NativeReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store);
        var journal = fixture.Source.Owner.ReadCanonicalCut();
        var failures = new List<Exception>();
        var first = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Source.Owner.SubmitIssuedEmbedded(issued, token));
        failures.Add(first);
        await Assert.That(first.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        var retained = fixture.Source.Owner.ReadStoredOutcome(command.CommandId);
        var original = retained.PreparedCommand ?? throw new InvalidOperationException(Missing);
        var committed = fixture.Source.Owner.ReadCanonicalCut();
        await Assert.That(committed.CommittedIndex).IsEqualTo(journal.CommittedIndex + 1);
        await Assert.That(retained.CanonicalSequence).IsEqualTo(committed.CommittedIndex);
        await RefusedAsync(fixture, original, image, position);
        var repeated = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Source.Owner.SubmitIssued(original));
        failures.Add(repeated);
        await Assert.That(repeated.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(fixture.Source.Owner.ReadCanonicalCut()).IsEqualTo(committed);
        await RefusedAsync(fixture, original, image, position);
        await KeyLoad.UnitTests.Features.StorageRecovery.Assertions.NativeReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(
            fixture.Store, identity, position);
        await SampleSequenceIntegrityAssertions.CounterAsync(fixture, NativeSerialization.Serialize(persistedSequence));
        await SampleSequenceIntegrityAssertions.ReadRefusalAsync(fixture, token);
        var recovered = fixture.Source.Owner.RecoverFaultOwnedRow(original, () => fixture.PutCounter(counter),
            failures, token, ErrorCode.Corruption);
        await QueueReadyAuthorityRefusalAssertions.RequireOriginalFailuresAsync(failures, ErrorCode.Corruption);
        await FinishAsync(fixture, command, original, recovered, committed.CommittedIndex);
        await Assert.That(fixture.Source.Owner.ReadCanonicalCut()).IsEqualTo(committed);
    }

    private static async Task RefusedAsync(SampleSequenceIntegrityFixture fixture, Command<DatabaseCommand> original,
        string image, long position)
    {
        var outcome = fixture.Database.ResolveOutcome(original);
        await Assert.That(Errors.Code(outcome.Problem)).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(outcome.Problem?.Detail).IsEqualTo(MissingOutcome);
        
        await Assert.That(outcome.Value).IsNull();
        await Assert.That(SampleChunkCorrectionBoundColdAssertions.Image(fixture.Store)).IsEqualTo(image);
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
    }

    private static async Task FinishAsync(SampleSequenceIntegrityFixture fixture, CommandRequest command,
        Command<DatabaseCommand> original, KeyLoad.Replication.StoredOutcome recovered, long expectedIndex)
    {
        await Assert.That(recovered.Index).IsEqualTo(expectedIndex);
        await Assert.That(recovered.Operation).IsNotNull();
        await SampleSequenceIntegrityAssertions.EqualAsync(command, NativeSerialization.Deserialize<CommandRequest>(original.Value!.Input.Span, NativeValidationProfile.PublicInputElements));
        await Assert.That(recovered.Operation!.CommandId).IsEqualTo(original.CommandId);
        await Assert.That(recovered.Operation.UserId!).IsEqualTo(P.Principal);
        await Assert.That(new DateTimeOffset(recovered.Operation.Timestamp)).IsEqualTo(new DateTimeOffset(original.Timestamp));
        await Assert.That(recovered.Operation.Value!.Input.Span.SequenceEqual(original.Value!.Input.Span)).IsTrue();
        var result = fixture.Source.Owner.SubmitIssued(original);
        var expected = new CommitReceipt(command.CommandId, new(fixture.Store.Identity.Incarnation, command.Partition.AtomicPartitionId, expectedIndex), [new("appendSamples", P.Set, P.Series, 3)], DurabilityProfile.ProcessDurable, 1);
        fixture.Originals.Add((original, result, command, expected));
        await SampleSequenceIntegrityAssertions.EqualAsync(expected, result.Get<CommitReceipt>());
        await Assert.That(result.Get<CommitReceipt>().Mutations.All(item => item.CompositionReferences.IsEmpty)).IsTrue();
        await SampleSequenceIntegrityAssertions.ReplayAsync(fixture, original, result);
    }
}
