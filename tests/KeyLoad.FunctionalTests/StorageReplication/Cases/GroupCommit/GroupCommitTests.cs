using KeyLoad.FunctionalTests.Authorization;
using KeyLoad.FunctionalTests.DatabaseComposition;
using KeyLoad.FunctionalTests.QueryExecution;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.StorageReplication;

/// <summary>AC-REP-008: consecutive canonical commands share one commit and one journal flush, each with its own
/// ordered sequence and the outcome it would have alone.</summary>
internal sealed class GroupCommitTests
{
    private const string First = "first";
    private const string Second = "second";
    private const string Created = "{\"version\":1}";
    private const string Updated = "{\"version\":2}";
    private const string Healthy = "{\"healthy\":true}";
    private const long NextSequence = 1;
    private const long OneFrame = 1;
    private const int FrameHeadroomNumerator = 3;
    private const int FrameHeadroomDenominator = 2;
    private const int LargeDocumentCharacters = 48 * 1024;

    [Test]
    public async Task AcRep008QueryPagesKeepPhysicalReadCutsAcrossGroupedFramesAndCursorContinuation()
    {
        using var fixture = new TestDatabase();
        fixture.Configure(GroupCommitCommands.Collection, ResourceKind.Collection);
        Command<DatabaseCommand>[] operations = [GroupCommitCommands.Put(fixture, First, Created),
            GroupCommitCommands.Put(fixture, Second, Created), GroupCommitCommands.Put(fixture, First, Updated, 1)];
        var token = GroupCommitCommands.Token;
        var prepared = fixture.Database.PrepareStorageOperations(operations, token);
        var sequence = fixture.Database.LastApplied;
        var position = fixture.Store.Position;
        var batch = fixture.Database.ApplyStorageOperations(prepared, sequence + NextSequence, token);
        var receipt = batch.Applied[^1].Result.Get<CommitReceipt>();
        await Assert.That(fixture.Store.Position).IsEqualTo(position + OneFrame);
        await Assert.That(receipt.Token.Position).IsEqualTo(batch.Position);
        await Assert.That(receipt.Token.Position).IsNotEqualTo(fixture.Store.Position);
        var observation = new QueryReadCutObservationCapture();
        var engine = new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution(),
            searchEngine: null, readCutObserver: observation);
        var request = new AstQueryRequest(fixture.Partition,
            new(GroupCommitCommands.Collection, null, [new("*", "*")], null, [new("/@id", false)], 1),
            AllowFullScan: true);

        var first = engine.ExecuteAst("root", request, cancellationToken: token);

        await Assert.That(first.CutPosition).IsEqualTo(position + OneFrame);
        await Assert.That(first.Rows.Select(row => row.EntityId)).IsEquivalentTo([First]);
        await Assert.That(first.Rows.Single().Json).IsEqualTo(Updated);
        await Assert.That(observation.Values.Single().ResultCutPosition).IsEqualTo(position + OneFrame);
        await Assert.That(observation.Values.Single().StorePosition).IsEqualTo(fixture.Store.Position);
        await Assert.That(first.Cursor).IsNotNull();
        var following = engine.ExecuteAst("root", request with { Cursor = first.Cursor }, cancellationToken: token);
        await Assert.That(following.CutPosition).IsEqualTo(position + OneFrame);
        await Assert.That(following.Rows.Select(row => row.EntityId)).IsEquivalentTo([Second]);
        await Assert.That(following.Rows.Single().Json).IsEqualTo(Created);
        await Assert.That(following.Cursor).IsNull();
        var replay = fixture.Database.ApplyStorageOperations([prepared[0]], batch.Position + NextSequence, token);
        await Assert.That(replay.Position).IsEqualTo(batch.Position);
        fixture.Commit(new PutDocument(GroupCommitCommands.Collection, Healthy, Healthy));
        var expired = Assert.ThrowsExactly<KeyLoadException>(() =>
            engine.ExecuteAst("root", request with { Cursor = first.Cursor }, cancellationToken: token));
        await Assert.That(expired.Code).IsEqualTo(ErrorCode.CursorExpired);
        var healthy = engine.ExecuteAst("root", request, cancellationToken: token);
        await Assert.That(healthy.CutPosition).IsEqualTo(fixture.Store.Position);
    }

    [Test]
    public async Task AcRep008ConsecutiveCommandsShareOneJournalFrameWithOrderedSequences()
    {
        using var fixture = new TestDatabase();
        fixture.Configure(GroupCommitCommands.Collection, ResourceKind.Collection);
        Command<DatabaseCommand>[] operations = [GroupCommitCommands.Put(fixture, First, Created),
            GroupCommitCommands.Put(fixture, Second, Created), GroupCommitCommands.Put(fixture, First, Updated, 1)];
        var token = GroupCommitCommands.Token;
        var prepared = fixture.Database.PrepareStorageOperations(operations, token);
        await Assert.That(prepared.Length).IsEqualTo(operations.Length);
        var sequence = fixture.Database.LastApplied;
        var position = fixture.Store.Position;

        var batch = fixture.Database.ApplyStorageOperations(prepared, sequence + NextSequence, token);

        await Assert.That(fixture.Store.Position).IsEqualTo(position + OneFrame);
        await Assert.That(batch.Position).IsEqualTo(sequence + operations.Length);
        await Assert.That(fixture.Database.LastApplied).IsEqualTo(batch.Position);
        await Assert.That(batch.Applied.Select(applied => applied.Sequence))
            .IsEquivalentTo([sequence + 1, sequence + 2, sequence + 3]);
        foreach (var applied in batch.Applied)
        {
            await Assert.That(applied.Result.IsSuccess).IsTrue();
        }
        await Assert.That(GroupCommitCommands.Read(fixture, First)!.Revision).IsEqualTo(2L);
        await Assert.That(GroupCommitCommands.Read(fixture, First)!.Json).IsEqualTo(Updated);
        await Assert.That(GroupCommitCommands.Read(fixture, Second)!.Revision).IsEqualTo(1L);
        var committed = fixture.Database.ReadLastCommittedCommands(token);
        await Assert.That(committed.Select(command => command.Sequence)).IsEquivalentTo(batch.Applied.Select(applied => applied.Sequence));
        await Assert.That(committed.Select(command => command.Command.CommandId)).IsEquivalentTo(operations.Select(operation => operation.CommandId));

        fixture.Commit(new PutDocument(GroupCommitCommands.Collection, Healthy, Healthy));
        var single = fixture.Database.ReadLastCommittedCommands(token);
        await Assert.That(single.Length).IsEqualTo(1);
        await Assert.That(single[0].Sequence).IsEqualTo(fixture.Database.LastApplied);
    }

    [Test]
    public async Task AcRep008RetryInsideABatchReplaysTheFirstOutcomeWithoutASecondSequence()
    {
        using var fixture = new TestDatabase();
        fixture.Configure(GroupCommitCommands.Collection, ResourceKind.Collection);
        var operation = GroupCommitCommands.Put(fixture, First, Created);
        var token = GroupCommitCommands.Token;
        var prepared = fixture.Database.PrepareStorageOperations([operation, operation], token);
        await Assert.That(prepared.Length).IsEqualTo(1);
        var sequence = fixture.Database.LastApplied;

        var batch = fixture.Database.ApplyStorageOperations([prepared[0], prepared[0]], sequence + NextSequence, token);

        await Assert.That(batch.Position).IsEqualTo(sequence + NextSequence);
        await Assert.That(batch.Applied.Select(applied => applied.Sequence)).IsEquivalentTo([sequence + 1, sequence + 1]);
        await Assert.That(OrleansSerialization.Serialize(batch.Applied[1].Result)
            .SequenceEqual(OrleansSerialization.Serialize(batch.Applied[0].Result))).IsTrue();
        await Assert.That(GroupCommitCommands.Read(fixture, First)!.Revision).IsEqualTo(1L);
        var retry = fixture.Database.PrepareStorageOperations([operation], token);
        await Assert.That(retry[0].Timestamp).IsEqualTo(prepared[0].Timestamp);
        var replay = fixture.Database.ApplyStorageOperations(retry, fixture.Database.LastApplied + NextSequence, token);
        await Assert.That(replay.Applied[0].Sequence).IsEqualTo(sequence + NextSequence);
        await Assert.That(fixture.Database.LastApplied).IsEqualTo(sequence + NextSequence);
    }

    [Test]
    public async Task AcRep008CommandThatOverflowsTheSharedFrameWaitsForItsOwnCommit()
    {
        var frameBytes = MeasureLargeCommandFrame();
        using var fixture = new TestDatabase(maxFrameBytes: frameBytes * FrameHeadroomNumerator / FrameHeadroomDenominator);
        fixture.Configure(GroupCommitCommands.Collection, ResourceKind.Collection);
        var token = GroupCommitCommands.Token;
        var prepared = fixture.Database.PrepareStorageOperations(
            [GroupCommitCommands.Put(fixture, First, LargeDocument()), GroupCommitCommands.Put(fixture, Second, LargeDocument())], token);
        var sequence = fixture.Database.LastApplied;

        var batch = fixture.Database.ApplyStorageOperations(prepared, sequence + NextSequence, token);

        await Assert.That(batch.Applied.Length).IsEqualTo(1);
        await Assert.That(batch.Position).IsEqualTo(sequence + NextSequence);
        await Assert.That(GroupCommitCommands.Read(fixture, Second)).IsNull();
        var next = fixture.Database.ApplyStorageOperations([prepared[1]], batch.Position + NextSequence, token);
        await Assert.That(next.Applied[0].Result.IsSuccess).IsTrue();
        await Assert.That(next.Applied[0].Sequence).IsEqualTo(sequence + 2);
        await Assert.That(GroupCommitCommands.Read(fixture, Second)!.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task AcRep008CopyThatMissedTheLastBatchCompletesFromItsRetainedCommands()
    {
        await DatabaseCompositionReplicaCut.RunAsync(SeedCollection, async (source, replica) =>
        {
            var token = GroupCommitCommands.Token;
            var prepared = source.Database.PrepareStorageOperations([GroupCommitCommands.Put(source, First, Created),
                GroupCommitCommands.Put(source, Second, Created)], token);
            var lagging = replica.Database.LastApplied;
            var batch = source.Database.ApplyStorageOperations(prepared, lagging + NextSequence, token);
            var missing = source.Database.ReadLastCommittedCommands(token).Where(command => command.Sequence > lagging)
                .Select(command => command.Command).ToArray();
            await Assert.That(missing.Length).IsEqualTo(prepared.Length);

            var completed = replica.Database.ApplyStorageOperations(missing, lagging + NextSequence, token);

            await Assert.That(completed.Position).IsEqualTo(batch.Position);
            await Assert.That(completed.Applied.Select(applied => applied.Sequence))
                .IsEquivalentTo(batch.Applied.Select(applied => applied.Sequence));
            await Assert.That(TeamRowPolicyAssertions.Image(replica.Store).SequenceEqual(TeamRowPolicyAssertions.Image(source.Store),
                StringComparer.Ordinal)).IsTrue();
        });
    }

    [Test]
    public async Task AcRep008BatchPreparationNeverMovesBusinessTimeBackwards()
    {
        var clock = new GroupCommitClock(TimeProvider.System.GetUtcNow());
        using var fixture = new TestDatabase(timeProvider: clock);
        fixture.Configure(GroupCommitCommands.Collection, ResourceKind.Collection);
        Command<DatabaseCommand>[] operations = [GroupCommitCommands.Put(fixture, First, Created),
            GroupCommitCommands.Put(fixture, Second, Created)];
        var token = GroupCommitCommands.Token;
        clock.Descending = true;

        var prepared = fixture.Database.PrepareStorageOperations(operations, token);
        clock.Descending = false;
        var batch = fixture.Database.ApplyStorageOperations(prepared, fixture.Database.LastApplied + NextSequence, token);

        await Assert.That(prepared[1].Timestamp).IsGreaterThanOrEqualTo(prepared[0].Timestamp);
        await Assert.That(batch.Applied.Length).IsEqualTo(operations.Length);
        foreach (var applied in batch.Applied)
        {
            await Assert.That(Errors.Code(applied.Result.Problem).Value).IsNull();
        }
        await Assert.That(GroupCommitCommands.Read(fixture, Second)!.Revision).IsEqualTo(1L);
    }

    private static TestDatabase SeedCollection()
    {
        var fixture = new TestDatabase();
        fixture.Configure(GroupCommitCommands.Collection, ResourceKind.Collection);
        return fixture;
    }

    private static int MeasureLargeCommandFrame()
    {
        using var probe = new TestDatabase();
        probe.Configure(GroupCommitCommands.Collection, ResourceKind.Collection);
        var journal = Path.Combine(probe.Directory, ZoneTreePersistenceFormat.JournalFileName);
        var before = new FileInfo(journal).Length;
        probe.Commit(new PutDocument(GroupCommitCommands.Collection, First, LargeDocument()));
        return checked((int)(new FileInfo(journal).Length - before));
    }

    private static string LargeDocument() => "{\"text\":\"" + new string('x', LargeDocumentCharacters) + "\"}";
}
