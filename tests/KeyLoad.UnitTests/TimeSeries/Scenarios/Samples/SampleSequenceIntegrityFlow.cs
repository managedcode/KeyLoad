using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.TimeSeries.SampleChunkCanonicalFixture;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleSequenceIntegrityFlow
{
    private const string Missing = "The native sequence operation did not retain its original entry.";
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
        await Assert.That(NativeSerialization.Deserialize<long>(counter)).IsEqualTo(2L);
        fixture.PutCounter(NativeSerialization.Serialize(persistedSequence));
        await SampleSequenceIntegrityRecovery.RunAsync(fixture, counter, persistedSequence, token);
        await CommitAsync(fixture, new AppendSamples(P.Set, P.Series,
            [new("sequence-fresh", P.Start.AddTicks(2), 6d)], P.Tags), "appendSamples", 4, token);
        await SampleSequenceIntegrityAssertions.HealthyAsync(fixture, merged: false, token);
        await CommitAsync(fixture, new MergeSampleChunkWindow(P.Set, P.Series, fixture.Source.WindowId, 6),
            "mergeSampleChunkWindow", 7, token);
        await WaveAsync(fixture, token);
        var image = SampleChunkCorrectionBoundColdAssertions.Image(fixture.Store);
        var position = fixture.Store.Position;
        var identity = KeyLoad.UnitTests.Features.StorageRecovery.Assertions.NativeReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store);
        for (var wave = 0; wave < ColdOwners; wave++)
        {
            token.ThrowIfCancellationRequested();
            fixture.Reopen();
            await Assert.That(SampleChunkCorrectionBoundColdAssertions.Image(fixture.Store)).IsEqualTo(image);
            await KeyLoad.UnitTests.Features.StorageRecovery.Assertions.NativeReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(
                fixture.Store, identity, position);
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
            await SampleSequenceIntegrityAssertions.ReplayAsync(fixture, original.Operation, original.Result);
        }
        await SampleSequenceIntegrityAssertions.HealthyAsync(fixture, merged: true, token);
    }

    private static async Task CommitAsync(SampleSequenceIntegrityFixture fixture, Mutation mutation,
        string kind, long revision, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), fixture.Source.Owner.Partition, [mutation]);
        var journal = fixture.Source.Owner.ReadCanonicalCut();
        var issued = fixture.Database.CreateCommand(OperationKind.Batch, command.CommandId, P.Principal,
            TimeProvider.System.GetUtcNow(), NativeSerialization.Serialize(command));
        var result = fixture.Source.Owner.SubmitIssuedEmbedded(issued, token);
        var operation = fixture.Source.Owner.ReadStoredOutcome(command.CommandId).PreparedCommand
            ?? throw new InvalidOperationException(Missing);
        var expected = new CommitReceipt(command.CommandId, new(fixture.Store.Identity.Incarnation, command.Partition.AtomicPartitionId, journal.CommittedIndex + 1), [new(kind, P.Set, P.Series, revision)], DurabilityProfile.ProcessDurable, 1);
        fixture.Originals.Add((operation, result, command, expected));
        await SampleSequenceIntegrityAssertions.EqualAsync(expected, result.Get<CommitReceipt>());
        await Assert.That(result.Get<CommitReceipt>().Mutations.All(item => item.CompositionReferences.IsEmpty)).IsTrue();
        await Assert.That(fixture.Source.Owner.ReadCanonicalCut().AppliedPosition).IsEqualTo(journal.CommittedIndex + 1);
    }
}
