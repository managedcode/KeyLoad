using System.Globalization;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Search;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal static class TextReadOnlySettlementFlow
{
    private const string Collection = "readonly-settlement";
    private const string Payload = "{\"text\":\"needle\"}";
    private const int Rows = 256;
    private const int Limit = 10;

    internal static async Task RunAsync(TestDatabase database, CancellationToken token)
    {
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit([.. Enumerable.Range(0, Rows).Select(index =>
            (Mutation)new PutDocument(Collection, Id(index), Payload))]);
        var position = database.Store.Position;
        var identity = database.Store.Identity;
        var root = Path.Combine(database.Directory, "readonly-close");
        await QuotaCloseAsync(database, root, token);
        await TamperedRetryAsync(database, root, token);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        database.JoinOwnedResources();
        using var cold = ZoneTreeExistingStore.Open(new(database.Directory) { Incarnation = identity.Incarnation },
            identity.NodeId, database.StorageExecution);
        var options = UnitTextOptions.Execution();
        using (var projection = new RuntimeTextProjection(root, identity.NodeId, options, new TextResourceOwnership(options)))
        {
            var engine = new SearchEngine(database.CreateConfiguredDatabase(cold), UnitExecutionOptions.QueryExecution(), projection);
            await RequireAsync(database, engine, token);
            await RequireAsync(database, engine, token);
        }
        ExclusiveFiles(root);
        await Assert.That(cold.Position).IsEqualTo(position);
    }

    private static async Task QuotaCloseAsync(TestDatabase database, string root, CancellationToken token)
    {
        var options = UnitTextOptions.Execution();
        var maximum = options.Value.MaximumDiskBytes;
        var inventoryEvents = 0;
        var publications = 0;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var projection = new RuntimeTextProjection(root, database.Store.Identity.NodeId, options,
                new TextResourceOwnership(options), faultObserver: stage =>
                {
                    if (stage == TextFaultStage.InventoryFlushed)
                    {
                        inventoryEvents++;
                    }
                    if (stage == TextFaultStage.ManifestPublished)
                    {
                        publications++;
                    }
                });
            try
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var engine = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
                    await RequireAsync(database, engine, token);
                    await RequireAsync(database, engine, token);
                    var generation = Directory.EnumerateDirectories(root).Single();
                    var manifest = Path.Combine(generation, TextProtocol.ManifestFile);
                    var original = await File.ReadAllBytesAsync(manifest, token);
                    var steady = TextFileIO.MeasureRegularFiles(root, options.Value.MaximumFiles, maximum, options);
                    var headroom = original.LongLength / 2;
                    var metadata = Directory.EnumerateFiles(generation, "0.json", SearchOption.AllDirectories).Single();
                    var metadataBytes = new FileInfo(metadata).Length
                        + new FileInfo(Path.Combine(generation, TextProtocol.OwnerFile)).Length;
                    await Assert.That(headroom).IsGreaterThan(checked(metadataBytes * 2));
                    await Assert.That(headroom).IsLessThan(original.LongLength);
                    options.Value.MaximumDiskBytes = checked(steady.Bytes + headroom);
                    options.Value.Validate();
                    await Assert.That(options.Value.MaximumDiskBytes).IsLessThan(maximum);
                    projection.Dispose();
                    await Assert.That(await File.ReadAllBytesAsync(manifest, token)).IsEquivalentTo(original, CollectionOrdering.Matching);
                    await Assert.That(File.Exists(Path.Combine(generation, TextProtocol.PendingManifestFile))).IsFalse();
                    await Assert.That(inventoryEvents).IsEqualTo(1);
                    await Assert.That(publications).IsEqualTo(1);
                    ExclusiveFiles(root);
                }, failures);
            }
            finally
            {
                options.Value.MaximumDiskBytes = maximum;
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task TamperedRetryAsync(TestDatabase database, string root, CancellationToken token)
    {
        var options = UnitTextOptions.Execution();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var projection = new RuntimeTextProjection(root, database.Store.Identity.NodeId, options, new TextResourceOwnership(options));
            string? manifest = null;
            byte[]? original = null;
            try
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var engine = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
                    await RequireAsync(database, engine, token);
                    await RequireAsync(database, engine, token);
                    var generation = Directory.EnumerateDirectories(root).Single();
                    manifest = Path.Combine(generation, TextProtocol.ManifestFile);
                    original = await File.ReadAllBytesAsync(manifest, token);
                    var envelope = OrleansSerialization.Deserialize<TextEnvelope>(original);
                    envelope.Payload[0] ^= 1;
                    var damaged = OrleansSerialization.Serialize(envelope);
                    await File.WriteAllBytesAsync(manifest, damaged, token);
                    for (var attempt = 0; attempt < 2; attempt++)
                    {
                        var failure = Assert.ThrowsExactly<KeyLoadException>(projection.Dispose);
                        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
                        await Assert.That(await File.ReadAllBytesAsync(manifest, token))
                            .IsEquivalentTo(damaged, CollectionOrdering.Matching);
                        await Assert.That(File.Exists(Path.Combine(generation, TextProtocol.PendingManifestFile))).IsFalse();
                        ExclusiveFiles(root);
                    }
                }, failures);
            }
            finally
            {
                if (manifest is not null && original is not null)
                {
                    await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(manifest, original, CancellationToken.None), failures);
                }
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RequireAsync(TestDatabase database, SearchEngine engine, CancellationToken token)
    {
        RankedDocument[] expected = [.. Enumerable.Range(0, Limit).Select(index => new RankedDocument(
            new(database.ExpectedReference(new(database.Partition, Collection, Id(index))), 1, Payload, false, []),
            1d / (61 + index)))];
        var actual = await engine.SearchAsync("root", new(database.Partition, Collection, "/text", "needle", Limit: Limit), token);
        await Assert.That(JsonDefaults.Serialize(actual)).IsEquivalentTo(JsonDefaults.Serialize(expected), CollectionOrdering.Matching);
    }

    private static string Id(int index) => "row-" + index.ToString("D4", CultureInfo.InvariantCulture);

    private static void ExclusiveFiles(string root)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        }
    }
}
