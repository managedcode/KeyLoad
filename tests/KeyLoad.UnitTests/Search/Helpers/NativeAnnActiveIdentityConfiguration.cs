namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeAnnActiveIdentityConfiguration
{
    private const string ConsumerName = "ann-active-session-identity";
    private const long StartAfter = 0;
    private const long Generation = 1;
    private const int Version = 1;
    private static readonly string[] Kinds = ["putDocument", "patchDocument", "deleteDocument", "putVector", "applyVectorProjection"];

    internal static (AnnMaintenanceRequest Request, ProjectionConsumerInfo Configured) Create(TestDatabase database)
    {
        var actual = database.Database.ReadAtomicPartitionPlacement(AnnProjectionPinTestSupport.Principal, new(Version, database.Partition));
        var placement = new PhysicalShardRecord(actual.PhysicalShardId, actual.Incarnation, actual.VoterIds, actual.PlacementEpoch);
        var consumer = AnnProjectionPinTestSupport.Consumer(database, ConsumerName);
        var space = AnnSeedTestSupport.Space();
        var request = new AnnMaintenanceRequest(Guid.NewGuid(), consumer, AnnSeedTestSupport.Collection,
            AnnSeedTestSupport.Field, space, Generation, database.Store.Identity.NodeId, placement, AnnMaintenanceMode.Build)
        {
            MaintenanceProfile = new(AnnSeedTestSupport.Collection, AnnSeedTestSupport.Field, space)
        };
        var definition = new ProjectionConsumerDefinition(request.IndexGeneration, [], [.. Kinds])
        {
            AnnMaintenance = request.MaintenanceProfile
        };
        var id = Guid.NewGuid();
        var configured = database.Submit(OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(id, consumer, definition, StartAfter), id: id).Get<ProjectionConsumerInfo>();
        return (request, configured);
    }
}
