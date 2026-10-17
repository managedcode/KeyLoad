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
        var issued = fixture.Database.CreateNativeOperation(OperationKind.Batch, command.CommandId, P.Principal,
            TimeProvider.System.GetUtcNow(), NativeSerialization.Serialize(command));
        var image = SampleChunkCorrectionBoundColdAssertions.Image(fixture.Store);
        var position = fixture.Store.Position;
        var identity = KeyLoad.UnitTests.Features.StorageRecovery.Assertions.NativeReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store);
        var journal = fixture.Source.Owner.ReadJournalCut();
        var failures = new List<Exception>();
        var first = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Source.Owner.SubmitIssuedEmbedded(issued, token));
        failures.Add(first);
        await Assert.That(first.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        var retained = fixture.Source.Owner.ReadRetainedEntry(command.CommandId);
        var original = retained.Operation ?? throw new InvalidOperationException(Missing);
        var committed = fixture.Source.Owner.ReadJournalCut();
        await Assert.That(committed.CommittedIndex).IsEqualTo(journal.CommittedIndex + 1);
        await Assert.That(retained.Index).IsEqualTo(committed.CommittedIndex);
        await RefusedAsync(fixture, original, image, position);
        var repeated = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Source.Owner.SubmitIssued(original));
        failures.Add(repeated);
        await Assert.That(repeated.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(fixture.Source.Owner.ReadJournalCut()).IsEqualTo(committed);
        await RefusedAsync(fixture, original, image, position);
        await KeyLoad.UnitTests.Features.StorageRecovery.Assertions.NativeReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(
            fixture.Store, identity, position);
        await SampleSequenceIntegrityAssertions.CounterAsync(fixture, NativeSerialization.Serialize(persistedSequence));
        await SampleSequenceIntegrityAssertions.ReadRefusalAsync(fixture, token);
        var recovered = fixture.Source.Owner.RecoverFaultOwnedRow(original, () => fixture.PutCounter(counter),
            failures, token, ErrorCode.Corruption);
        await QueueReadyAuthorityRefusalAssertions.RequireOriginalFailuresAsync(failures, ErrorCode.Corruption);
        await FinishAsync(fixture, command, original, recovered, committed.CommittedIndex);
        await Assert.That(fixture.Source.Owner.ReadJournalCut()).IsEqualTo(committed);
    }

    private static async Task RefusedAsync(SampleSequenceIntegrityFixture fixture, ReplicatedOperation original,
        string image, long position)
    {
        var outcome = fixture.Database.ResolveOutcome(original);
        await Assert.That(outcome.Error).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(outcome.SafeDetail).IsEqualTo(MissingOutcome);
        await Assert.That(outcome.Json).IsNull();
        await Assert.That(outcome.NativeValue).IsNull();
        await Assert.That(SampleChunkCorrectionBoundColdAssertions.Image(fixture.Store)).IsEqualTo(image);
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
    }

    private static async Task FinishAsync(SampleSequenceIntegrityFixture fixture, CommandRequest command,
        ReplicatedOperation original, KeyLoad.Replication.ReplicaEntry recovered, long expectedIndex)
    {
        await Assert.That(recovered.Index).IsEqualTo(expectedIndex);
        await Assert.That(recovered.Operation).IsNotNull();
        await SampleSequenceIntegrityAssertions.EqualAsync(command, JsonDefaults.Deserialize<CommandRequest>(original.PayloadJson));
        await Assert.That(recovered.Operation!.Id).IsEqualTo(original.Id);
        await Assert.That(recovered.Operation.PrincipalId).IsEqualTo(P.Principal);
        await Assert.That(recovered.Operation.EvaluatedAt).IsEqualTo(original.EvaluatedAt);
        await Assert.That(recovered.Operation.NativePayload.Span.SequenceEqual(original.NativePayload.Span)).IsTrue();
        var result = fixture.Source.Owner.SubmitIssued(original);
        var expected = new CommitReceipt(command.CommandId,
            new(fixture.Store.Identity.Incarnation, command.Partition.AtomicPartitionId, expectedIndex, 1),
            [new("appendSamples", P.Set, P.Series, 3)], DurabilityProfile.ProcessDurable);
        fixture.Originals.Add((original, result, command, expected));
        await SampleSequenceIntegrityAssertions.EqualAsync(expected, result.Get<CommitReceipt>());
        await Assert.That(result.Get<CommitReceipt>().Mutations.All(item => item.CompositionReferences.IsEmpty)).IsTrue();
        await SampleSequenceIntegrityAssertions.ReplayAsync(fixture, original, result);
    }
}
