using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryAdapterIndexedFlow
{
    private const int ColdWaves = 2;
    private const int FirstWave = 0;
    private static readonly Guid SeedId = new("a02d5f0a-c3a0-429b-b8e6-dd38be6dd6b4");

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                fixture.Configure(QueryAdapterIndexedCorpus.Collection, ResourceKind.Collection,
                    indexes: [new(QueryAdapterTestTokens.Status, [QueryAdapterTestTokens.StatusPath])]);
                var seed = QueryAdapterIndexedCorpus.Seed();
                var command = new CommandRequest(SeedId, fixture.Partition, [.. seed.Documents]);
                var operation = new ReplicatedOperation(command.CommandId, OperationKind.Batch, QueryAdapterIndexedCorpus.Principal,
                    default, JsonSerializer.Serialize(command, JsonDefaults.Options));
                var receipt = fixture.Database.ApplyEmbedded(operation, cancellationToken: default).Get<CommitReceipt>();
                await Assert.That(receipt.CommandId).IsEqualTo(SeedId);
                await QueryWorkloadCoverageAssertions.AssertDocumentCoverage(seed.Coverage,
                    QueryAdapterIndexedCorpus.StatusCount, QueryAdapterIndexedCorpus.NumericValueCount);
                await WaveAsync(fixture.Database, fixture.Store, fixture.Partition, operation, receipt);
                var image = SqlInnerJoinRetainedIdentityFlow.Image(fixture.Store);
                var position = fixture.Store.Position;
                var identity = JsonDefaults.Serialize(fixture.Store.Identity);
                fixture.Store.Dispose();
                for (var wave = FirstWave; wave < ColdWaves; wave++)
                {
                    await ColdAsync(fixture, operation, receipt, image, position, identity);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ColdAsync(TestDatabase fixture, ReplicatedOperation operation, CommitReceipt receipt,
        string[] image, long position, byte[] identity)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution,
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await SqlOffsetRefusals.UnchangedAsync(store, image, position, identity);
                await WaveAsync(fixture.CreateConfiguredDatabase(store), store, fixture.Partition, operation, receipt);
            }, failures);
            await ServerFailureObserver.ObserveAsync(() => SqlOffsetRefusals.UnchangedAsync(store, image, position, identity), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WaveAsync(DatabaseEngine database, ZoneTreeStore store, PartitionRef partition,
        ReplicatedOperation operation, CommitReceipt receipt)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => QueryAdapterIndexedTrials.RunAsync(
            new(database, UnitExecutionOptions.QueryExecution()), store, partition), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var replay = database.ApplyEmbedded(operation, cancellationToken: default).Get<CommitReceipt>();
            await Assert.That(JsonDefaults.Serialize(replay)).IsEquivalentTo(JsonDefaults.Serialize(receipt), CollectionOrdering.Matching);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => SqlOffsetRefusals.UnchangedAsync(store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
