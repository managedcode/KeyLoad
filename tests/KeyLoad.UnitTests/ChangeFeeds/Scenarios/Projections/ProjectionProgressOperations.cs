using ManagedCode.Communication;
namespace KeyLoad.UnitTests.ChangeFeeds;

internal static class ProjectionProgressOperations
{
    internal static ProjectionConsumerRef Configure(TestDatabase db, string name = "projection")
    {
        var consumer = new ProjectionConsumerRef(db.Partition, name);
        var id = Guid.NewGuid();
        db.Submit(OperationKind.ConfigureProjectionConsumer, new ConfigureProjectionConsumerRequest(id, consumer,
            new(1, ["orders"], ["putDocument"])), id: id).Get<ProjectionConsumerInfo>();
        return consumer;
    }
    internal static Result<object> Complete(TestDatabase db, ProjectionConsumerRef consumer, ProjectionBatch batch, params Mutation[] effects)
    {
        var id = Guid.NewGuid();
        return db.Submit(OperationKind.CommitProjectionBatch,
            new CommitProjectionBatchRequest(id, consumer, batch.Token, [.. effects]), id: id);
    }
    internal static void ConfigureResources(TestDatabase db)
    {
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("derived", ResourceKind.Collection);
    }
}
