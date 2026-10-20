using KeyLoad.Clustering;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class TextMissingSettledAuthorityFlow
{
    private const string CorruptionDetail = "The execution text projection is corrupt.";
    private const string Principal = "root";
    private const string UkrainianQuery = "ПРИВІТ";
    private const long Revision = 1;
    private const double Score = 1d / 61d;

    internal static async Task RejectAndRepairAsync(TestDatabase database,
        TextIndexMaintenanceRequest restore, string path, byte[] original, CancellationToken token)
    {
        var options = UnitTextOptions.Execution();
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            cancellationToken: token);
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException();
        var manifest = TextIncrementalMetadata.ReadManifest(directory, options.Value.MaximumDiskBytes, budget);
        await Assert.That(manifest.Bootstrap).IsFalse();
        await Assert.That(manifest.LastSettledCheckpointRequest).IsNotNull();
        var position = database.Store.Position;
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                TextIncrementalMetadata.Publish(directory,
                    manifest with
                    {
                        LastSettledCheckpointRequest = null
                    },
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
        await using var runtime = new TextMaintenanceTestRuntime(database);
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

    internal static async Task HealthyAsync(TestDatabase database, TextMaintenanceTestRuntime runtime,
        TextIndexMaintenanceRequest request, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var projection = new TextSelectedProjection(TextBilingualAudit.Open(database),
                runtime.Owner, runtime.Owner);
            var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
            var selection = new TextIndexSelection(request.Consumer, request.IndexGeneration);
            await LiteralAsync(search, database, selection, UkrainianQuery,
                TextBilingualAudit.UkrainianId, TextBilingualAudit.UkrainianJson, token);
            await LiteralAsync(search, database, selection, TextBilingualAudit.EnglishQuery,
                TextBilingualAudit.EnglishId, TextBilingualAudit.EnglishJson, token);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }

    private static async Task LiteralAsync(SearchEngine search, TestDatabase database,
        TextIndexSelection selection, string query, string id, string json, CancellationToken token)
    {
        var rows = await search.SearchAsync(Principal,
            TextBilingualAudit.Request(database.Partition, query) with
            {
                TextIndex = selection
            }, token);
        var row = await Assert.That(rows).HasSingleItem();
        var expected = new DocumentResult(database.ExpectedReference(new(database.Partition, TextBilingualAudit.Collection, id)), Revision, json, false, []);
        await Assert.That(JsonDefaults.Serialize(row.Document).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(row.Score).IsEqualTo(Score);
    }
}
