using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextMissingSettledAuthorityFlow
{
    private const string CorruptionDetail = "The native text projection is corrupt.";
    private const string Principal = "root";
    private const string UkrainianQuery = "ПРИВІТ";
    private const long Revision = 1;
    private const double Score = 1d / 61d;

    internal static async Task RejectAndRepairAsync(TestDatabase database,
        TextIndexMaintenanceRequest restore, string path, byte[] original, CancellationToken token)
    {
        var options = UnitNativeTextOptions.Execution();
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            cancellationToken: token);
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException();
        var manifest = NativeTextIncrementalMetadata.ReadManifest(directory, options.Value.MaximumDiskBytes, budget);
        await Assert.That(manifest.Bootstrap).IsFalse();
        await Assert.That(manifest.LastSettledCheckpointRequest).IsNotNull();
        var position = database.Store.Position;
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                NativeTextIncrementalMetadata.Publish(directory,
                    manifest with { LastSettledCheckpointRequest = null },
                    options.Value.MaximumDiskBytes, budget, options);
                var missing = await File.ReadAllBytesAsync(path, token);
                await RejectAsync(database, restore, path, missing, image, position, failures, token);
            }, failures);
        }
        finally
        {
            await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(path, original, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RejectAsync(TestDatabase database, TextIndexMaintenanceRequest restore,
        string path, byte[] missing, string[] image, long position, List<Exception> failures, CancellationToken token)
    {
        await using var runtime = new NativeTextMaintenanceTestRuntime(database);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => runtime.PhaseAsync(database,
                restore, TextMaintenanceCapabilityKind.Begin, token: token)) ?? throw new InvalidOperationException();
            await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(error.Message).IsEqualTo(CorruptionDetail);
            await Assert.That(database.Store.Position).IsEqualTo(position);
            await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
            await Assert.That(await File.ReadAllBytesAsync(path, token)).IsEquivalentTo(missing, CollectionOrdering.Matching);
        }, failures);
    }

    internal static async Task HealthyAsync(TestDatabase database, NativeTextMaintenanceTestRuntime runtime,
        TextIndexMaintenanceRequest request, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var projection = new NativeTextSelectedProjection(NativeTextBilingualAudit.Open(database),
                runtime.Owner, runtime.Owner);
            var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
            var selection = new TextIndexSelectionV1(request.Consumer, request.IndexGeneration);
            await LiteralAsync(search, database.Partition, selection, UkrainianQuery,
                NativeTextBilingualAudit.UkrainianId, NativeTextBilingualAudit.UkrainianJson, token);
            await LiteralAsync(search, database.Partition, selection, NativeTextBilingualAudit.EnglishQuery,
                NativeTextBilingualAudit.EnglishId, NativeTextBilingualAudit.EnglishJson, token);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }

    private static async Task LiteralAsync(SearchEngine search, PartitionRef partition,
        TextIndexSelectionV1 selection, string query, string id, string json, CancellationToken token)
    {
        var rows = await search.SearchAsync(Principal,
            NativeTextBilingualAudit.Request(partition, query) with { TextIndex = selection }, token);
        var row = await Assert.That(rows).HasSingleItem();
        var expected = new DocumentResult(new(partition, NativeTextBilingualAudit.Collection, id), Revision, json, false, []);
        await Assert.That(JsonDefaults.Serialize(row.Document).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(row.Score).IsEqualTo(Score);
    }
}
