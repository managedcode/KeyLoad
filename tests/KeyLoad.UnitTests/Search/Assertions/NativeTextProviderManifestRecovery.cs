using KeyLoad.Query;
using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextProviderManifestRecovery
{
    private const string Collection = "native-text-restart";
    private const string TextPath = "/text";
    private const string Query = "needle";
    private const string DocumentId = "one";
    private const string Json = """{"text":"needle"}""";
    private const string Principal = "root";
    private const long Revision = 1;
    private const double SingleBranchScore = 1d / 61;

    internal static async Task VerifyAsync(TestDatabase database, string indexDirectory, string manifest,
        byte[] original, string[] canonical, long position, CancellationToken cancellationToken)
    {
        await UnchangedAsync(database, canonical, position);
        await File.WriteAllBytesAsync(manifest, original, cancellationToken);
        await Assert.That(await File.ReadAllBytesAsync(manifest, cancellationToken))
            .IsEquivalentTo(original, CollectionOrdering.Matching);
        await HealthyAsync(database, indexDirectory, cancellationToken);
        await UnchangedAsync(database, canonical, position);
        await HealthyAsync(database, indexDirectory, cancellationToken);
        await UnchangedAsync(database, canonical, position);
    }

    private static async Task HealthyAsync(TestDatabase database, string indexDirectory,
        CancellationToken cancellationToken)
    {
        using var projection = new NativeTextProjection(indexDirectory,
            UnitExecutionOptions.DatabaseLimits(database.Database.Limits), database.Store.Identity.NodeId,
            UnitNativeTextOptions.Execution());
        var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
        var results = await search.SearchAsync(Principal,
            new(database.Partition, Collection, TextPath, Query), cancellationToken);
        var row = await Assert.That(results).HasSingleItem();
        var expected = new DocumentResult(new(database.Partition, Collection, DocumentId), Revision, Json, false, []);
        await Assert.That(JsonDefaults.Serialize(row.Document).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(row.Score).IsEqualTo(SingleBranchScore);
        await Assert.That(row.Explanation).IsNull();
    }

    private static async Task UnchangedAsync(TestDatabase database, string[] canonical, long position)
    {
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store))
            .IsEquivalentTo(canonical, CollectionOrdering.Matching);
    }
}
