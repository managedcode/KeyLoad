
namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleRollupLeaseDropTests
{
    [Test]
    public async Task AcSeries022NativeQueuedDropWaitsForRealReadLeaseThenReplaysAndRecreatesHealthyBucket()
    {
        using var db = new TestDatabase();
        SampleRollupWholeFlow.Seed(db);
        db.Commit(SampleRollupWholeFlow.Refresh(0));
        var raw = SampleRollupWholeFlow.Raw(db);
        var id = Guid.NewGuid();
        var drop = new DropSampleRollup(SampleRollupWholeFlow.Set, SampleRollupWholeFlow.Series,
            SampleRollupWholeFlow.Start, SampleRollupWholeFlow.End, 1);
        var outcome = await SampleRollupLeaseDropFlow.RunAsync(db, id, drop);
        var receipt = outcome.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(1);
        await Assert.That(receipt.Mutations[0]).IsEqualTo(new MutationReceipt(SampleRollupProtocol.DropKind,
            SampleRollupWholeFlow.Set, SampleRollupWholeFlow.Series, 2));
        await Assert.That(SampleRollupWholeFlow.Raw(db)).IsEqualTo(raw);
        await Assert.That(SampleRollupWholeFlow.Read(db)).IsEqualTo(new SampleRollupResult(2, null));
        var image = SampleRollupWholeFlow.Image(db);
        var position = db.Store.Position;
        var retry = SampleRollupWholeFlow.Commit(db, id, drop);
        await Assert.That(SampleRollupWholeFlow.Outcome(retry)).IsEqualTo(SampleRollupWholeFlow.Outcome(outcome));
        await Assert.That(SampleRollupWholeFlow.Image(db)).IsEqualTo(image);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await Assert.That(db.Store.GateDiagnostics.WaitingWriters).IsEqualTo(0);
        db.Commit(SampleRollupWholeFlow.Refresh(2));
        await SampleRollupWholeFlow.Literal(SampleRollupWholeFlow.Read(db), 3, 6, 3, 12, 2, 6, 4);
        await Assert.That(SampleRollupWholeFlow.Raw(db)).IsEqualTo(raw);
    }
}
