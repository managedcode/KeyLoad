namespace KeyLoad.UnitTests.Features.Search;

internal static class TextMaintenanceRequestFixture
{
    internal static async Task<TextIndexMaintenanceRequest> CreateAsync(
        TestDatabase fixture, CancellationToken cancellationToken)
    {
        var database = fixture;
        var consumer = new ProjectionConsumerRef(database.Partition, TextMaintenanceTestValues.Consumer);
        var configureId = Guid.NewGuid();
        _ = await TextMaintenanceCommit.ExecuteAsync<ProjectionConsumerInfo>(fixture,
            OperationKind.ConfigureProjectionConsumer, new ConfigureProjectionConsumerRequest(configureId, consumer,
                new(TextMaintenanceTestValues.Generation, [TextBilingualAudit.Collection],
                    [MutationDiscriminatorNames.PutDocument, MutationDiscriminatorNames.PatchDocument,
                        MutationDiscriminatorNames.DeleteDocument])), configureId, cancellationToken);
        var placement = database.Database.ReadAtomicPartitionPlacement(TextMaintenanceTestValues.Principal,
            database.Partition);
        return new(Guid.NewGuid(), consumer, TextBilingualAudit.Collection, TextMaintenanceTestValues.Field,
            TextMaintenanceTestValues.Generation, database.Store.Identity.NodeId,
            new(placement.PhysicalShardId, placement.Incarnation),
            TextIndexMaintenanceMode.Build);
    }
}
