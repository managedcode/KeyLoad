using ManagedCode.Communication.Commands;
using P = KeyLoad.UnitTests.TimeSeries.SampleChunkCanonicalFixture;

namespace KeyLoad.UnitTests.TimeSeries;

internal static class SampleSequenceIntegrityRecovery
{
    private const string Missing = "The successful original execution sequence outcome is absent.";
    private const string MissingOutcome = "The applied command has no durable outcome.";

    internal static async Task RunAsync(SampleSequenceIntegrityFixture fixture, byte[] counter, long persistedSequence,
        string healthyImage, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), fixture.Source.Owner.Partition,
            [SampleSequenceIntegrityFlow.Append()]);
        var issued = fixture.Database.CreateCommand(OperationKind.Batch, command.CommandId, P.Principal,
            TimeProvider.System.GetUtcNow(), OrleansSerialization.Serialize(command));
        var image = SampleChunkCorrectionBoundColdAssertions.Image(fixture.Store);
        var cut = fixture.ReadCanonicalCut();
        var identity = KeyLoad.UnitTests.StorageRecovery.Assertions.ReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store);
        var first = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.ApplyEmbedded(issued, token));
        await Assert.That(first.Code).IsEqualTo(ErrorCode.Corruption);
        await RefusedAsync(fixture, issued, image, cut);
        var repeated = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.ApplyEmbedded(issued, token));
        await Assert.That(repeated.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(repeated.Message).IsEqualTo(first.Message);
        await RefusedAsync(fixture, issued, image, cut);
        await KeyLoad.UnitTests.StorageRecovery.Assertions.ReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(
            fixture.Store, identity, cut.StorePosition);
        await SampleSequenceIntegrityAssertions.CounterAsync(fixture, OrleansSerialization.Serialize(persistedSequence));
        await SampleSequenceIntegrityAssertions.ReadRefusalAsync(fixture, token);
        fixture.PutCounter(counter);
        await SampleSequenceIntegrityAssertions.CounterAsync(fixture, counter);
        await Assert.That(SampleChunkCorrectionBoundColdAssertions.Image(fixture.Store)).IsEqualTo(healthyImage);
        await Assert.That(fixture.ReadCanonicalCut()).IsEqualTo((cut.AppliedPosition, cut.StorePosition + 1));
        await Assert.That(OutcomeStoreOracle.ReadStored(fixture.Store, issued)).IsNull();
        await FinishAsync(fixture, command, issued, token);
    }

    private static async Task RefusedAsync(SampleSequenceIntegrityFixture fixture, Command<DatabaseCommand> original,
        string image, (long AppliedPosition, long StorePosition) cut)
    {
        await Assert.That(OutcomeStoreOracle.ReadStored(fixture.Store, original)).IsNull();
        var outcome = fixture.Database.ResolveOutcome(original);
        await Assert.That(Errors.Code(outcome.Problem).Value).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(outcome.Problem?.Detail).IsEqualTo(MissingOutcome);
        await Assert.That(outcome.Value).IsNull();
        await Assert.That(SampleChunkCorrectionBoundColdAssertions.Image(fixture.Store)).IsEqualTo(image);
        await Assert.That(fixture.ReadCanonicalCut()).IsEqualTo(cut);
    }

    private static async Task FinishAsync(SampleSequenceIntegrityFixture fixture, CommandRequest command,
        Command<DatabaseCommand> issued, CancellationToken token)
    {
        var cut = fixture.ReadCanonicalCut();
        var result = fixture.Database.ApplyEmbedded(issued, token);
        var outcomeBytes = fixture.Store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.Key(issued)))
            ?? throw new InvalidOperationException(Missing);
        var recovered = OrleansSerialization.Deserialize<StoredOutcome>(outcomeBytes);
        var original = recovered.PreparedCommand;
        var expectedSequence = cut.AppliedPosition + 1;
        await Assert.That(recovered.CanonicalSequence).IsEqualTo(expectedSequence);
        await SampleSequenceIntegrityAssertions.EqualAsync(command,
            OrleansSerialization.Deserialize<CommandRequest>(original.Value!.Input.Span, ValidationProfile.PublicInputElements));
        await Assert.That(original.CommandId).IsEqualTo(issued.CommandId);
        await Assert.That(original.UserId!).IsEqualTo(P.Principal);
        await Assert.That(original.Timestamp >= issued.Timestamp).IsTrue();
        await Assert.That(original.Value!.Kind).IsEqualTo(OperationKind.Batch);
        await Assert.That(original.Value.Input.Span.SequenceEqual(issued.Value!.Input.Span)).IsTrue();
        var expected = new CommitReceipt(command.CommandId,
            new(fixture.Store.Identity.Incarnation, command.Partition.AtomicPartitionId, expectedSequence),
            [new("appendSamples", P.Set, P.Series, 3)], DurabilityProfile.ProcessDurable, 1);
        fixture.Originals.Add((original, result, command, expected, outcomeBytes));
        await SampleSequenceIntegrityAssertions.EqualAsync(expected, result.Get<CommitReceipt>());
        await SampleSequenceIntegrityAssertions.EqualAsync(expected, recovered.Result.Get<CommitReceipt>());
        await Assert.That(result.Get<CommitReceipt>().Mutations.All(item => item.CompositionReferences.IsEmpty)).IsTrue();
        await Assert.That(fixture.ReadCanonicalCut()).IsEqualTo((expectedSequence, cut.StorePosition + 1));
        await SampleSequenceIntegrityAssertions.ReplayAsync(fixture, original, result, outcomeBytes, token);
    }
}
