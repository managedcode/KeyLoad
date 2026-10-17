namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextMaintenanceRequestFixture
{
    internal static async Task<TextIndexMaintenanceRequest> CreateAsync(
        TestDatabase fixture, CancellationToken cancellationToken)
    {
        var database = fixture;
        var consumer = new ProjectionConsumerRef(database.Partition, NativeTextMaintenanceTestValues.Consumer);
        var configureId = Guid.NewGuid();
        _ = await NativeTextMaintenanceCommit.ExecuteAsync<ProjectionConsumerInfo>(fixture,
            OperationKind.ConfigureProjectionConsumer, new ConfigureProjectionConsumerRequest(configureId, consumer,
                new(NativeTextMaintenanceTestValues.Generation, [NativeTextBilingualAudit.Collection],
                    [MutationDiscriminatorNames.PutDocument, MutationDiscriminatorNames.PatchDocument,
                        MutationDiscriminatorNames.DeleteDocument])), configureId, cancellationToken);
        var placement = database.Database.ReadAtomicPartitionPlacement(NativeTextMaintenanceTestValues.Principal,
            new(NativeTextMaintenanceTestValues.PlacementVersion, database.Partition));
        return new(Guid.NewGuid(), consumer, NativeTextBilingualAudit.Collection, NativeTextMaintenanceTestValues.Field,
            NativeTextMaintenanceTestValues.Generation, database.Store.Identity.NodeId,
            new(placement.PhysicalShardId, placement.Incarnation, placement.VoterIds, placement.PlacementEpoch),
            TextIndexMaintenanceMode.Build);
    }
}
