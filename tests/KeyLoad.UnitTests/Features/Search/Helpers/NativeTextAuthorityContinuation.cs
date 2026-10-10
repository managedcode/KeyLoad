using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextAuthorityContinuation
{
    private const string Root = "root";
    private const string Reader = "reader";
    private const string HealthyId = "healthy";
    private const string HealthyTerm = "repaired";
    private const string OldTerm = "needle";
    private const string ChangedTerm = "changed";
    private const string HealthyJson = "{\"secret\":\"repaired\",\"other\":\"healthy\"}";
    private const string PublicJson = "{\"other\":\"healthy\"}";
    private const string PutKind = "putDocument";
    private const string Field = "/secret";
    private const string ProjectionDirectory = "native-text";
    private const long Revision = 1;
    private const double Score = 1d / 61d;

    internal static async Task RunAsync(TestDatabase database, NativeTextProjection projection,
        SearchEngine search, SearchRequest request, string[] original, long revokedCut,
        Action repair, CancellationToken token)
    {
        await UnchangedAsync(database.Store, original, revokedCut);
        repair();
        var command = new CommandRequest(Guid.NewGuid(), database.Partition,
            [new PutDocument(request.Collection, HealthyId, HealthyJson, Access: new(Reader))]);
        var receipt = database.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(database.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsEqualTo(database.Store.Position);
        var expected = new MutationReceipt(PutKind, request.Collection, HealthyId, Revision);
        var mutation = await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(NativeSerialization.Serialize(mutation).SequenceEqual(
            NativeSerialization.Serialize(expected))).IsTrue();
        var bytes = QueueWholeFlowStorage.Bytes(database.Store);
        var cut = database.Store.Position;
        var replay = database.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
        await UnchangedAsync(database.Store, bytes, cut);
        await LiteralAsync(search, request, token);
        await UnchangedAsync(database.Store, bytes, cut);
        await ColdAsync(database, projection, request, command, receipt, bytes, cut, token);
    }

    private static async Task ColdAsync(TestDatabase database, NativeTextProjection original,
        SearchRequest request, CommandRequest command, CommitReceipt receipt, string[] bytes,
        long cut, CancellationToken token)
    {
        var identity = database.Store.Identity;
        ZoneTreeStore? reopened = null;
        NativeTextProjection? projection = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                original.Dispose();
                database.Store.Dispose();
                reopened = OpenStore(database);
                await Assert.That(reopened.Identity.NodeId).IsEqualTo(identity.NodeId);
                await Assert.That(reopened.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                await Assert.That(reopened.Identity.ReadGeneration).IsGreaterThanOrEqualTo(identity.ReadGeneration);
                await UnchangedAsync(reopened, bytes, cut);
                var owner = NativeTextBilingualAudit.Owner(reopened);
                var replay = owner.ApplyEmbedded(new(command.CommandId, OperationKind.Batch, Root, default,
                    JsonSerializer.Serialize(command, JsonDefaults.Options)), cancellationToken: token).Get<CommitReceipt>();
                await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
                await UnchangedAsync(reopened, bytes, cut);
                projection = OpenProjection(database);
                await LiteralAsync(new(owner, UnitExecutionOptions.QueryExecution(), projection), request, token);
                await UnchangedAsync(reopened, bytes, cut);
            }, failures);
        }
        finally
        {
            if (projection is not null)
            { ServerFailureObserver.Observe(projection.Dispose, failures); }
            if (reopened is not null)
            { ServerFailureObserver.Observe(reopened.Dispose, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static ZoneTreeStore OpenStore(TestDatabase database)
        => new(new(database.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());

    private static NativeTextProjection OpenProjection(TestDatabase database)
        => new(Path.Combine(database.Directory, ProjectionDirectory), UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            database.Store.Identity.NodeId, UnitNativeTextOptions.Execution());

    private static async Task UnchangedAsync(ZoneTreeStore store, string[] bytes, long cut)
    {
        await Assert.That(store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(bytes, CollectionOrdering.Matching);
    }

    private static async Task LiteralAsync(SearchEngine search, SearchRequest request, CancellationToken token)
    {
        var rows = await search.SearchAsync(Reader, request with { Text = HealthyTerm }, token);
        var expected = new RankedDocument(new(new(request.Partition, request.Collection, HealthyId),
            Revision, PublicJson, true, [Field]), Score);
        await Assert.That(JsonDefaults.Serialize(rows).SequenceEqual(JsonDefaults.Serialize(new[] { expected }))).IsTrue();
        await Assert.That(await search.SearchAsync(Reader, request with { Text = OldTerm }, token)).IsEmpty();
        await Assert.That(await search.SearchAsync(Reader, request with { Text = ChangedTerm }, token)).IsEmpty();
    }
}
