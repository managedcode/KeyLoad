using KeyLoad.Query;
using KeyLoad.Server;
using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class TopicSqlNativeSeed
{
    private const int NoFailures = 0;

    internal static TestDatabase Create(DatabaseLimits? limits = null)
    {
        var database = new TestDatabase(limits);
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => Configure(database), failures);
        if (failures.Count > NoFailures)
        {
            ServerFailureObserver.Observe(database.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        return database;
    }

    private static void Configure(TestDatabase database)
    {
        SqlModelViewTestSupport.Configure(database, TopicSqlProtocol.Topic, ResourceKind.Topic,
            fields: [new(TopicSqlProtocol.PayloadPath, TopicSqlProtocol.Classification,
                TopicSqlProtocol.ReadGrant, TopicSqlProtocol.UseGrant)],
            headerPolicies: [new(TopicSqlProtocol.HeaderPath, TopicSqlProtocol.Classification,
                TopicSqlProtocol.HeaderRead, TopicSqlProtocol.HeaderUse)]);
        database.Configure(TopicSqlProtocol.Empty, ResourceKind.Topic);
    }

    internal static CommandRequest Command(TestDatabase database) => new(Guid.NewGuid(), database.Partition,
        [new PublishTopic(TopicSqlProtocol.Topic,
            [new(TopicSqlProtocol.First, TopicSqlProtocol.Created, TopicSqlProtocol.Payload, TopicSqlProtocol.Headers),
             new(TopicSqlProtocol.Second, TopicSqlProtocol.Updated, TopicSqlProtocol.NextPayload, TopicSqlProtocol.Headers)])]);

    internal static CommitReceipt Publish(TestDatabase database, CommandRequest command)
        => database.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();

    internal static EventSourcePage Read(TestDatabase database) => database.Database.ReadEventSource(TopicSqlProtocol.Root,
        new(new(database.Partition, TopicSqlProtocol.Topic, EventSourceKind.Topic)));

    internal static EventSourcePage Read(TestDatabase database, long afterPosition) => database.Database.ReadEventSource(TopicSqlProtocol.Root,
        new(new(database.Partition, TopicSqlProtocol.Topic, EventSourceKind.Topic), AfterPosition: afterPosition));

    internal static QueryEngine Engine(TestDatabase database) => new(database.Database, UnitExecutionOptions.QueryExecution());
    internal static QueryRequest Request(TestDatabase database, string sql = TopicSqlProtocol.Sql)
        => new(database.Partition, sql, AllowFullScan: true);
    internal static AstQueryRequest Ast(TestDatabase database) => new(database.Partition,
        new(TopicSqlProtocol.Topic, null, [new(TopicSqlProtocol.Star, TopicSqlProtocol.Star)], null,
            [new(TopicSqlProtocol.PositionPath, false)], TopicSqlProtocol.PageLimit,
            ModelSource: new(ModelQuerySourceKind.TopicEvents, TopicSqlProtocol.Topic)), AllowFullScan: true);

    internal static PrincipalRecord Grant(TestDatabase database, Capability capability, long epoch,
        ImmutableArray<string> fields = default)
        => database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(TopicSqlProtocol.Reader,
            database.Partition.TenantId, [new(database.Partition.DatabaseId, TopicSqlProtocol.Topic, capability)],
            fields.IsDefault ? [] : fields)
        { PolicyEpoch = epoch })).Get<PrincipalRecord>();
}
