using KeyLoad.Server;
using P = KeyLoad.UnitTests.TimeSeries.SampleChunkCanonicalFixture;

namespace KeyLoad.UnitTests.TimeSeries;

internal static class SampleSequenceIntegrityFlow
{
    private const string Missing = "The execution sequence operation did not retain its original outcome.";
    private const int ColdOwners = 2;
    internal static async Task RunAsync(long persistedSequence, CancellationToken token)
    {
        var failures = new List<Exception>();
        string? root = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new SampleSequenceIntegrityFixture();
            root = fixture.Source.Owner.Directory;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                try
                {
                    await CompleteAsync(fixture, persistedSequence, token);
                }
                catch (Exception)
                {
                    fixture.RetainEvidence();
                    throw;
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(root).IsNotNull();
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    private static async Task CompleteAsync(SampleSequenceIntegrityFixture fixture, long persistedSequence, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await CommitAsync(fixture, new OpenSampleChunkWindow(P.Set, P.Series, fixture.Source.WindowId,
            P.Start, P.Until), "openSampleChunkWindow", 1, token);
        await CommitAsync(fixture, new AppendSamples(P.Set, P.Series, P.Initial, P.Tags), "appendSamples", 2, token);
        await CommitAsync(fixture, new SealSampleChunkWindow(P.Set, P.Series, fixture.Source.WindowId, 3),
            "sealSampleChunkWindow", 4, token);
        var counter = fixture.Store.Read(view => view.ReadOwnedValue(fixture.Counter))
            ?? throw new InvalidOperationException(Missing);
        await Assert.That(OrleansSerialization.Deserialize<long>(counter)).IsEqualTo(2L);
        var healthyImage = SampleChunkCorrectionBoundColdAssertions.Image(fixture.Store);
        fixture.PutCounter(OrleansSerialization.Serialize(persistedSequence));
        await SampleSequenceIntegrityRecovery.RunAsync(fixture, counter, persistedSequence, healthyImage, token);
        await CommitAsync(fixture, new AppendSamples(P.Set, P.Series,
            [new("sequence-fresh", P.Start.AddTicks(2), 6d)], P.Tags), "appendSamples", 4, token);
        await SampleSequenceIntegrityAssertions.HealthyAsync(fixture, merged: false, token);
        await CommitAsync(fixture, new MergeSampleChunkWindow(P.Set, P.Series, fixture.Source.WindowId, 6),
            "mergeSampleChunkWindow", 7, token);
        await WaveAsync(fixture, token);
        var image = SampleChunkCorrectionBoundColdAssertions.Image(fixture.Store);
        var cut = fixture.ReadCanonicalCut();
        var identity = KeyLoad.UnitTests.StorageRecovery.Assertions.ReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store);
        for (var wave = 0; wave < ColdOwners; wave++)
        {
            token.ThrowIfCancellationRequested();
            fixture.Reopen();
            await Assert.That(SampleChunkCorrectionBoundColdAssertions.Image(fixture.Store)).IsEqualTo(image);
            await KeyLoad.UnitTests.StorageRecovery.Assertions.ReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(
                fixture.Store, identity, cut.StorePosition);
            await Assert.That(fixture.ReadCanonicalCut()).IsEqualTo(cut);
            await WaveAsync(fixture, token);
        }
    }

    internal static AppendSamples Append()
        => new(P.Set, P.Series, [new("sequence-repaired", P.Start.AddTicks(1), -2d)], P.Tags);

    private static async Task WaveAsync(SampleSequenceIntegrityFixture fixture, CancellationToken token)
    {
        await SampleSequenceIntegrityAssertions.HealthyAsync(fixture, merged: true, token);
        foreach (var original in fixture.Originals)
        {
            await SampleSequenceIntegrityAssertions.ReplayAsync(fixture, original.Operation, original.Result, original.OutcomeBytes, token);
        }
        await SampleSequenceIntegrityAssertions.HealthyAsync(fixture, merged: true, token);
    }

    private static async Task CommitAsync(SampleSequenceIntegrityFixture fixture, Mutation mutation,
        string kind, long revision, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), fixture.Source.Owner.Partition, [mutation]);
        var cut = fixture.ReadCanonicalCut();
        var issued = fixture.Database.CreateCommand(OperationKind.Batch, command.CommandId, P.Principal,
            TimeProvider.System.GetUtcNow(), OrleansSerialization.Serialize(command));
        var result = fixture.Database.ApplyEmbedded(issued, token);
        var outcomeBytes = fixture.Store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.Key(issued)))
            ?? throw new InvalidOperationException(Missing);
        var outcome = OrleansSerialization.Deserialize<KeyLoad.Core.StoredOutcome>(outcomeBytes);
        var operation = outcome.PreparedCommand;
        var expectedSequence = cut.AppliedPosition + 1;
        var expected = new CommitReceipt(command.CommandId,
            new(fixture.Store.Identity.Incarnation, command.Partition.AtomicPartitionId, expectedSequence),
            [new(kind, P.Set, P.Series, revision)], DurabilityProfile.ProcessDurable, 1);
        fixture.Originals.Add((operation, result, command, expected, outcomeBytes));
        await SampleSequenceIntegrityAssertions.EqualAsync(expected, result.Get<CommitReceipt>());
        await Assert.That(outcome.CanonicalSequence).IsEqualTo(expectedSequence);
        await Assert.That(result.Get<CommitReceipt>().Mutations.All(item => item.CompositionReferences.IsEmpty)).IsTrue();
        await Assert.That(fixture.ReadCanonicalCut()).IsEqualTo((expectedSequence, cut.StorePosition + 1));
    }
}
