namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class ChangeFeedTestActions
{
    internal static ProjectionConsumerRef Consumer(TestDatabase db, string name = "text") => new(db.Partition, name);
    internal static ProjectionConsumerInfo Configure(TestDatabase db, ProjectionConsumerRef consumer, long? after = null)
    {
        var id = Guid.NewGuid();
        return db.Submit(OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(id, consumer, new(1, ["orders"], ["putDocument", "patchDocument", "deleteDocument"]), after), id: id).Get<ProjectionConsumerInfo>();
    }
    internal static ProjectionBatchResult Complete(TestDatabase db, ProjectionConsumerRef consumer, ProjectionBatch batch, params Mutation[] effects)
    {
        var id = Guid.NewGuid();
        return db.Submit(OperationKind.CommitProjectionBatch,
            new CommitProjectionBatchRequest(id, consumer, batch.Token, [.. effects]), id: id).Get<ProjectionBatchResult>();
    }
    internal static void Reader(TestDatabase db, string id = "reader", string? owner = null, string[]? grants = null, long epoch = 1)
        => db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(id, "tenant",
            [new("database", "orders", Capability.DocumentsRead | Capability.ChangesRead | Capability.Query)], [.. (grants ?? [])])
        {
            OwnerId = owner,
            RestrictRows = owner is not null,
            PolicyEpoch = epoch
        })).Get<PrincipalRecord>();
}
