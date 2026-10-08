
namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeAnnMaintenanceTestData
{
    internal const string Consumer = "ann-native-lifecycle";
    internal const long Generation = 1;
    internal const int Version = 1;
    internal const int PageLimit = 1;
    internal const int PageBytes = 4_194_304;
    internal const int SnapshotBound = 512;
    private static readonly string[] Kinds = ["putDocument", "patchDocument", "deleteDocument", "putVector", "applyVectorProjection"];

    internal static AnnMaintenanceRequest Pin(TestDatabase database)
    {
        var consumer = AnnProjectionPinTestSupport.Consumer(database, Consumer);
        var id = Guid.NewGuid();
        _ = database.Submit(OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(id, consumer, new(Generation, [], [.. Kinds]), StartAfter: 0), id: id).Get<ProjectionConsumerInfo>();
        var actual = database.Database.ReadAtomicPartitionPlacement(AnnProjectionPinTestSupport.Principal, new(Version, database.Partition));
        var placement = new PhysicalShardRecord(actual.PhysicalShardId, actual.Incarnation, actual.VoterIds, actual.PlacementEpoch);
        return new(Guid.NewGuid(), consumer, AnnSeedTestSupport.Collection, AnnSeedTestSupport.Field,
            AnnSeedTestSupport.Space(), Generation, database.Store.Identity.NodeId, placement, AnnMaintenanceMode.Build);
    }
    internal static ProjectionBatch Read(TestDatabase database, AnnMaintenanceRequest request, long upper)
        => database.Database.ReadProjectionBatch(AnnProjectionPinTestSupport.Principal, new(request.Consumer, PageLimit, PageBytes, upper));
    internal static CommitProjectionBatchRequest Intent(ProjectionBatch page)
        => new(Guid.NewGuid(), page.Consumer.Consumer, page.Token, []);
    internal static ProjectionBatchResult Commit(TestDatabase database, CommitProjectionBatchRequest intent)
        => database.Submit(OperationKind.CommitProjectionBatch, intent, id: intent.CommandId).Get<ProjectionBatchResult>();
    internal static (string Key, string Value)[] Snapshot(TestDatabase database) => database.Store.Read(view =>
    {
        var page = view.Scan([], SnapshotBound);
        if (page.HasMore)
        { throw new InvalidOperationException("The native ANN lifecycle fixture exceeds its complete state bound."); }
        return page.Records.Select(row => (Convert.ToHexString(row.Key.Span), Convert.ToHexString(row.Value.Span))).ToArray();
    });
}
