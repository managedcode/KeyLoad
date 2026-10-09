using KeyLoad.Orleans;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

/// <summary>Settles only the actual retained original intent/ACK and verifies complete literal selected pages through native reopen.</summary>
internal static class NativeTextIncrementalIntentContinuation
{
    private const string Root = "root";
    private const string UkrainianQuery = "ПРИВІТ";
    private const long OriginalRevision = 1;
    private const double Score = 1d / 61d;

    internal static async Task RunAsync(TestDatabase database, TextIndexMaintenanceRequest request,
        CommitProjectionBatchRequest original, CancellationToken token)
    {
        await NativeTextIncrementalIntentOwner.RunAsync(database, async runtime =>
        {
            var begin = await runtime.PhaseAsync(database, request, TextMaintenanceCapabilityKind.Begin, token: token);
            await Assert.That(NativeSerialization.Serialize(begin.OriginalCheckpointIntent!).SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
            _ = await runtime.PhaseAsync(database, request, TextMaintenanceCapabilityKind.ApplyIntent, token: token);
            var receipt = await NativeTextMaintenanceCommit.ExecuteAsync<ProjectionBatchResult>(database,
                OperationKind.CommitProjectionBatch, original, original.CommandId, token);
            await Assert.That(receipt.Receipt.CommandId).IsEqualTo(original.CommandId);
            _ = await runtime.PhaseAsync(database, request, TextMaintenanceCapabilityKind.SettleCheckpoint, acknowledged: receipt, token: token);
            var complete = await runtime.PhaseAsync(database, request, TextMaintenanceCapabilityKind.Verify, token: token);
            await Assert.That(complete.Checkpoint).IsEqualTo(complete.ThroughSequence);
            await LiteralAsync(database, runtime, request, token);
            var image = QueueWholeFlowStorage.Bytes(database.Store);
            var position = database.Store.Position;
            var replay = await NativeTextMaintenanceCommit.ExecuteAsync<ProjectionBatchResult>(database,
                OperationKind.CommitProjectionBatch, original, original.CommandId, token);
            await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
            await Assert.That(database.Store.Position).IsEqualTo(position);
            await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        });
        await NativeTextIncrementalIntentOwner.RunAsync(database, async cold =>
        {
            _ = await cold.PhaseAsync(database, request, TextMaintenanceCapabilityKind.Begin, token: token);
            await LiteralAsync(database, cold, request, token);
        });
    }

    internal static async Task LiteralAsync(TestDatabase database, NativeTextMaintenanceTestRuntime runtime,
        TextIndexMaintenanceRequest request, CancellationToken token)
    {
        NativeTextSelectedProjection? projection = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                projection = new NativeTextSelectedProjection(NativeTextBilingualAudit.Open(database), runtime.Owner, runtime.Owner);
                var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
                var image = QueueWholeFlowStorage.Bytes(database.Store);
                var position = database.Store.Position;
                await PageAsync(search, request, UkrainianQuery, NativeTextBilingualAudit.UkrainianId, NativeTextBilingualAudit.UkrainianJson, token);
                await PageAsync(search, request, NativeTextBilingualAudit.EnglishQuery, NativeTextBilingualAudit.EnglishId, NativeTextBilingualAudit.EnglishJson, token);
                await Assert.That(database.Store.Position).IsEqualTo(position);
                await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
            }, failures);
        }
        finally
        {
            if (projection is not null)
            { ServerFailureObserver.Observe(projection.Dispose, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task PageAsync(SearchEngine search, TextIndexMaintenanceRequest request,
        string text, string id, string json, CancellationToken token)
    {
        var query = NativeTextBilingualAudit.Request(request.Consumer.Partition, text)
            with
        { TextIndex = new(request.Consumer, request.IndexGeneration) };
        var actual = await search.SearchAsync(Root, query, token);
        RankedDocument[] expected = [new(new(new(request.Consumer.Partition, request.Collection, id), OriginalRevision, json, false, []), Score)];
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
