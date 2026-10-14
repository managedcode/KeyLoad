namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ProjectionProgressOperations
{
    internal static ProjectionConsumerRef Configure(TestDatabase db, string name = "projection-v1")
    {
        var consumer = new ProjectionConsumerRef(db.Partition, name);
        var id = Guid.NewGuid();
        db.Submit(OperationKind.ConfigureProjectionConsumer, new ConfigureProjectionConsumerRequest(id, consumer,
            new(1, ["orders"], ["putDocument"])), id: id).Get<ProjectionConsumerInfo>();
        return consumer;
    }
    internal static OperationResult Complete(TestDatabase db, ProjectionConsumerRef consumer, ProjectionBatch batch, params Mutation[] effects)
    {
        var id = Guid.NewGuid();
        return db.Submit(OperationKind.CommitProjectionBatch,
            new CommitProjectionBatchRequest(id, consumer, batch.Token, [.. effects]), id: id);
    }
    internal static OperationResult Write(TestDatabase db, params Mutation[] effects)
    {
        var id = Guid.NewGuid();
        return db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition, [.. effects]), id: id);
    }
    internal static OutboxHead Purge(TestDatabase db, long through)
    {
        var id = Guid.NewGuid();
        return db.Submit(OperationKind.PurgeOutbox, new PurgeOutboxRequest(id, db.Partition, through), id: id).Get<OutboxHead>();
    }
    internal static void ConfigureResources(TestDatabase db)
    {
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("derived", ResourceKind.Collection);
    }
}
