using KeyLoad.Replication;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaTermMetadataReadTests
{
    private const int WarmLookups = 512;
    private const long InitialTerm = 3;

    [Test]
    public async Task LargeValidRetainedEntryUsesOneBorrowedColdLookupAndNoWarmPointLookups()
    {
        using var fixture = new ReplicaTermMetadataFixture();
        fixture.Log.SaveTermAndVote(InitialTerm, null);
        var operation = ReplicaTermMetadataFixture.Operation(new string('x', ReplicaTermMetadataFixture.LargePayloadCharacters));
        var entry = new ReplicaEntry(1, InitialTerm, operation);
        var encoded = ReplicaProtocolCodec.Serialize(entry);
        fixture.Log.Append([entry]);
        fixture.Log.Commit(1);

        var before = fixture.Store.GetReadDiagnostics();
        await Assert.That(fixture.Log.TermAt(1)).IsEqualTo(InitialTerm);
        var cold = fixture.Store.GetReadDiagnostics();
        await Assert.That(cold.BorrowedPointLookups - before.BorrowedPointLookups).IsEqualTo(1);
        await Assert.That(cold.OwnedPointLookups - before.OwnedPointLookups).IsEqualTo(0);
        await Assert.That(cold.PointExaminedBytes - before.PointExaminedBytes)
            .IsEqualTo(ReplicaProtocol.EntryStorageKey(1).Length + encoded.Length);

        RepeatWarmLookups(fixture, InitialTerm);
        var warm = fixture.Store.GetReadDiagnostics();
        await Assert.That(warm.BorrowedPointLookups - cold.BorrowedPointLookups).IsEqualTo(0);
        await Assert.That(warm.OwnedPointLookups - cold.OwnedPointLookups).IsEqualTo(0);
        await Assert.That(warm.PointExaminedBytes - cold.PointExaminedBytes).IsEqualTo(0);
    }

    [Test]
    public async Task AlternatingRetainedIndicesReplaceTheSingleObservation()
    {
        using var fixture = new ReplicaTermMetadataFixture();
        fixture.Log.SaveTermAndVote(4, null);
        fixture.Log.Append([new(1, 2, null), new(2, 3, null), new(3, 4, null)]);
        fixture.Log.Commit(3);
        var before = fixture.Store.GetReadDiagnostics();

        await Assert.That(fixture.Log.TermAt(1)).IsEqualTo(2);
        await Assert.That(fixture.Log.TermAt(1)).IsEqualTo(2);
        await Assert.That(fixture.Log.TermAt(2)).IsEqualTo(3);
        await Assert.That(fixture.Log.TermAt(2)).IsEqualTo(3);
        await Assert.That(fixture.Log.TermAt(1)).IsEqualTo(2);
        var after = fixture.Store.GetReadDiagnostics();

        await Assert.That(after.BorrowedPointLookups - before.BorrowedPointLookups).IsEqualTo(3);
        await Assert.That(after.OwnedPointLookups - before.OwnedPointLookups).IsEqualTo(0);
    }

    private static void RepeatWarmLookups(ReplicaTermMetadataFixture fixture, long expectedTerm)
    {
        for (var attempt = 0; attempt < WarmLookups; attempt++)
        {
            if (fixture.Log.TermAt(1) != expectedTerm)
            {
                throw new InvalidOperationException("A warm term lookup returned an unexpected term.");
            }
        }
    }
}
