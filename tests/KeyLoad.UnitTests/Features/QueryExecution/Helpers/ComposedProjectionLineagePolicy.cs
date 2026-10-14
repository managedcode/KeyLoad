namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class ComposedProjectionLineagePolicy
{
    internal const string ScoreUse = "score.use";
    internal const string ScoreRead = "score.read";
    private const long SchemaVersionStep = 1;

    internal static async Task<(CommandRequest Command, CommitReceipt Receipt)> RunAsync(TestDatabase database, CancellationToken token)
    {
        await ConfigureResourceAsync(database);
        var principal = new PrincipalRecord(ComposedProjectionLineageFlow.Reader, database.Partition.TenantId,
            [new(database.Partition.DatabaseId, SqlQueueReferenceJoinFixture.Queue, Capability.Query | Capability.QueueInspect),
                new(database.Partition.DatabaseId, SqlQueueReferenceJoinFixture.Target, Capability.Query | Capability.DocumentsRead)],
            [SqlQueueReferenceJoinFixture.RefUse, ScoreRead, ScoreUse])
        {
            RestrictRows = true,
            OwnerId = SqlQueueReferenceJoinFixture.Reader
        };
        var persisted = await PersistAsync(database, principal);
        await ComposedProjectionLineageFlow.VerifyAsync(database.Database, database.Store, database.Partition, masked: true, repaired: false, token);
        var repaired = await PersistAsync(database, persisted with
        {
            PolicyEpoch = persisted.PolicyEpoch + 1,
            FieldGrants = [SqlQueueReferenceJoinFixture.RefUse, SqlQueueReferenceJoinFixture.RefRead, SqlQueueReferenceJoinFixture.NameRead, ScoreRead, ScoreUse]
        });
        await ComposedProjectionLineageFlow.VerifyAsync(database.Database, database.Store, database.Partition, masked: false, repaired: false, token);
        var predicateRevoked = await PersistAsync(database, repaired with
        {
            PolicyEpoch = repaired.PolicyEpoch + 1,
            FieldGrants = [SqlQueueReferenceJoinFixture.RefUse,
            SqlQueueReferenceJoinFixture.RefRead, SqlQueueReferenceJoinFixture.NameRead, ScoreRead]
        });
        await ComposedProjectionLineageFlow.DeniedAsync(database, token);
        repaired = await PersistAsync(database, predicateRevoked with
        {
            PolicyEpoch = predicateRevoked.PolicyEpoch + 1,
            FieldGrants = [SqlQueueReferenceJoinFixture.RefUse,
            SqlQueueReferenceJoinFixture.RefRead, SqlQueueReferenceJoinFixture.NameRead, ScoreRead, ScoreUse]
        });
        await ComposedProjectionLineageFlow.VerifyAsync(database.Database, database.Store, database.Partition, masked: false, repaired: false, token);
        var revoked = await PersistAsync(database, repaired with
        {
            PolicyEpoch = repaired.PolicyEpoch + 1,
            FieldGrants = [SqlQueueReferenceJoinFixture.RefRead, SqlQueueReferenceJoinFixture.NameRead, ScoreRead, ScoreUse]
        });
        await ComposedProjectionLineageFlow.DeniedAsync(database, token);
        await PersistAsync(database, revoked with
        {
            PolicyEpoch = revoked.PolicyEpoch + 1,
            FieldGrants = [SqlQueueReferenceJoinFixture.RefUse, SqlQueueReferenceJoinFixture.RefRead, SqlQueueReferenceJoinFixture.NameRead, ScoreRead, ScoreUse]
        });
        var command = new CommandRequest(Guid.NewGuid(), database.Partition,
            [new PutDocument(SqlQueueReferenceJoinFixture.Target, "m", SqlQueueReferenceJoinFixture.RepairedJson,
                Access: new(OwnerId: SqlQueueReferenceJoinFixture.Reader))]);
        var receipt = database.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
        await ComposedProjectionLineageFlow.VerifyAsync(database.Database, database.Store, database.Partition, masked: false, repaired: true, token);
        return (command, receipt);
    }

    private static async Task<PrincipalRecord> PersistAsync(TestDatabase database, PrincipalRecord expected)
    {
        var persisted = database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(expected)).Get<PrincipalRecord>();
        await Assert.That(JsonDefaults.Serialize(persisted).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        var stored = database.Store.Read(view => global::KeyLoad.Storage.StorageRecords.GetRecord<PrincipalRecord>(
            view, global::KeyLoad.Core.KeySpace.Principal(expected.Id)));
        await Assert.That(JsonDefaults.Serialize(stored).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        return persisted;
    }

    private static async Task ConfigureResourceAsync(TestDatabase database)
    {
        var originalResource = database.Store.Read(view => database.Database.Resource(view, database.Partition,
            SqlQueueReferenceJoinFixture.Queue, ResourceKind.WorkQueue));
        var resource = originalResource with
        {
            SchemaVersion = checked(originalResource.SchemaVersion + SchemaVersionStep),
            FieldPolicies = [new("/to", "reference", RawReadGrant: SqlQueueReferenceJoinFixture.RefRead,
                RawUseGrant: SqlQueueReferenceJoinFixture.RefUse), new("/score", "predicate",
                RawReadGrant: ScoreRead, RawUseGrant: ScoreUse)]
        };
        var persistedResource = database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, resource)
            {
                ExpectedSchemaVersion = originalResource.SchemaVersion
            }).Get<ResourceDefinition>();
        await Assert.That(JsonDefaults.Serialize(persistedResource).AsSpan().SequenceEqual(JsonDefaults.Serialize(resource))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(database.Store.Read(view => database.Database.Resource(view,
            database.Partition, SqlQueueReferenceJoinFixture.Queue, ResourceKind.WorkQueue))).AsSpan().SequenceEqual(JsonDefaults.Serialize(resource))).IsTrue();
    }
}
