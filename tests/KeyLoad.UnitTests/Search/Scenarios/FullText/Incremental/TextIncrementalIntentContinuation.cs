using KeyLoad.Clustering;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Search;
using KeyLoad.UnitTests.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Search;

/// <summary>Settles only the actual retained original intent/ACK and verifies complete literal selected pages through execution reopen.</summary>
internal static class TextIncrementalIntentContinuation
{
    private const string Root = "root";
    private const string UkrainianQuery = "ПРИВІТ";
    private const long OriginalRevision = 1;
    private const double Score = 1d / 61d;

    internal static async Task RunAsync(TestDatabase database, TextIndexMaintenanceRequest request,
        CommitProjectionBatchRequest original, CancellationToken token)
    {
        await TextIncrementalIntentOwner.RunAsync(database, async runtime =>
        {
            var begin = await runtime.PhaseAsync(database, request, TextMaintenanceCapabilityKind.Begin, token: token);
            await Assert.That(OrleansSerialization.Serialize(begin.OriginalCheckpointIntent!).SequenceEqual(OrleansSerialization.Serialize(original))).IsTrue();
            _ = await runtime.PhaseAsync(database, request, TextMaintenanceCapabilityKind.ApplyIntent, token: token);
            var receipt = await TextMaintenanceCommit.ExecuteAsync<ProjectionBatchResult>(database,
                OperationKind.CommitProjectionBatch, original, original.CommandId, token);
            await Assert.That(receipt.Receipt.CommandId).IsEqualTo(original.CommandId);
            _ = await runtime.PhaseAsync(database, request, TextMaintenanceCapabilityKind.SettleCheckpoint, acknowledged: receipt, token: token);
            var complete = await runtime.PhaseAsync(database, request, TextMaintenanceCapabilityKind.Verify, token: token);
            await Assert.That(complete.Checkpoint).IsEqualTo(complete.ThroughSequence);
            await LiteralAsync(database, runtime, request, token);
            var image = QueueWholeFlowStorage.Bytes(database.Store);
            var position = database.Store.Position;
            var replay = await TextMaintenanceCommit.ExecuteAsync<ProjectionBatchResult>(database,
                OperationKind.CommitProjectionBatch, original, original.CommandId, token);
            await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(OrleansSerialization.Serialize(receipt))).IsTrue();
            await Assert.That(database.Store.Position).IsEqualTo(position);
            await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        });
        await TextIncrementalIntentOwner.RunAsync(database, async cold =>
        {
            _ = await cold.PhaseAsync(database, request, TextMaintenanceCapabilityKind.Begin, token: token);
            await LiteralAsync(database, cold, request, token);
        });
    }

    internal static async Task LiteralAsync(TestDatabase database, TextMaintenanceTestRuntime runtime,
        TextIndexMaintenanceRequest request, CancellationToken token)
    {
        TextSelectedProjection? projection = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                projection = new TextSelectedProjection(TextBilingualAudit.Open(database), runtime.Owner, runtime.Owner);
                var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
                var image = QueueWholeFlowStorage.Bytes(database.Store);
                var position = database.Store.Position;
                await PageAsync(database, search, request, UkrainianQuery, TextBilingualAudit.UkrainianId, TextBilingualAudit.UkrainianJson, token);
                await PageAsync(database, search, request, TextBilingualAudit.EnglishQuery, TextBilingualAudit.EnglishId, TextBilingualAudit.EnglishJson, token);
                await Assert.That(database.Store.Position).IsEqualTo(position);
                await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
            }, failures);
        }
        finally
        {
            if (projection is not null)
            {
                ServerFailureObserver.Observe(projection.Dispose, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task PageAsync(TestDatabase database, SearchEngine search, TextIndexMaintenanceRequest request,
        string text, string id, string json, CancellationToken token)
    {
        var query = TextBilingualAudit.Request(request.Consumer.Partition, text)
            with
        {
            TextIndex = new(request.Consumer, request.IndexGeneration)
        };
        var actual = await search.SearchAsync(Root, query, token);
        RankedDocument[] expected = [new(new(database.ExpectedReference(new(request.Consumer.Partition, request.Collection, id)), OriginalRevision, json, false, []), Score)];
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
