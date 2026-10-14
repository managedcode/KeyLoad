using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

/// <summary>One canonical atomic partition with typed rows and their existing linked models.</summary>
internal sealed record RelationalSqlRf3Scenario(PartitionRef Partition, ResourceDefinition TableDefinition)
{
    internal EntityRef First => new(Partition, RelationalSqlRf3Tokens.Table, RelationalSqlRf3Tokens.FirstId);
    internal EntityRef Second => new(Partition, RelationalSqlRf3Tokens.Table, RelationalSqlRf3Tokens.SecondId);
    internal StreamRef Stream => new(Partition, RelationalSqlRf3Tokens.Streams, RelationalSqlRf3Tokens.StreamId);
    private static VectorSpace Space => new(RelationalSqlRf3Tokens.VectorSpace, RelationalSqlRf3Tokens.Dimension,
        DistanceMetric.DotProduct, RelationalSqlRf3Tokens.VectorModel, RelationalSqlRf3Tokens.VectorVersion);
    internal static async Task<RelationalSqlRf3Scenario> CreateAsync(KeyLoadClient administrator, CancellationToken cancellationToken)
        => await CreateAsync(administrator, new PartitionRef(RelationalSqlRf3Tokens.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            RelationalSqlRf3Tokens.Database, RelationalSqlRf3Tokens.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat)), cancellationToken);
    internal static async Task<RelationalSqlRf3Scenario> CreateAsync(KeyLoadClient administrator,
        PartitionRef partition, CancellationToken cancellationToken)
    {
        var table = new ResourceDefinition(RelationalSqlRf3Tokens.Table, ResourceKind.Collection, partition.TransactionDomainId)
        {
            RelationalSchema = new(RelationalSqlRf3Tokens.Key, RelationalSqlRf3Tokens.Columns),
            Indexes = [new(RelationalSqlRf3Tokens.TitleIndex, [RelationalSqlRf3Tokens.TitlePath], Unique: true)]
        };
        var persisted = await ConfigureAsync(administrator, partition, table, cancellationToken);
        await Assert.That(persisted.RelationalSchema!.PrimaryKey).IsEqualTo(RelationalSqlRf3Tokens.Key);
        await SqlRf3Protocol.EqualAsync(RelationalSqlRf3Tokens.Columns, persisted.RelationalSchema.Columns);
        await Assert.That(persisted.Indexes[0].Unique).IsTrue();
        foreach (var (name, kind) in new[]
        {
            (RelationalSqlRf3Tokens.Documents, ResourceKind.Collection), (RelationalSqlRf3Tokens.Streams, ResourceKind.StreamSet),
            (RelationalSqlRf3Tokens.Queue, ResourceKind.WorkQueue), (RelationalSqlRf3Tokens.Graph, ResourceKind.Graph),
            (RelationalSqlRf3Tokens.SeriesSet, ResourceKind.TimeSeries), (RelationalSqlRf3Tokens.Blobs, ResourceKind.BlobStore)
        })
        {
            await ConfigureAsync(administrator, partition, new(name, kind, partition.TransactionDomainId), cancellationToken);
        }
        return new(partition, persisted);
    }
    internal CommandRequest Command(params Mutation[] effects)
        => new(Guid.NewGuid(), Partition, [.. effects]);
    internal CommandRequest LinkedModelsCommand() => Command(
        new PutDocument(RelationalSqlRf3Tokens.Table, RelationalSqlRf3Tokens.FirstId, RelationalSqlRf3Tokens.FirstRow),
        new PutDocument(RelationalSqlRf3Tokens.Table, RelationalSqlRf3Tokens.SecondId, RelationalSqlRf3Tokens.SecondRow),
        new PutDocument(RelationalSqlRf3Tokens.Documents, RelationalSqlRf3Tokens.DocumentId, RelationalSqlRf3Tokens.EmptyJson),
        new UpsertEdge(RelationalSqlRf3Tokens.Graph, RelationalSqlRf3Tokens.EdgeId, First, Second, RelationalSqlRf3Tokens.EdgeLabel),
        new PutVector(RelationalSqlRf3Tokens.Table, RelationalSqlRf3Tokens.FirstId, RelationalSqlRf3Tokens.VectorField,
            [RelationalSqlRf3Tokens.UnitVector, RelationalSqlRf3Tokens.ZeroVector], Space, RelationalSqlRf3Tokens.FirstRevision),
        new AppendEvents(RelationalSqlRf3Tokens.Streams, RelationalSqlRf3Tokens.StreamId,
            [new(RelationalSqlRf3Tokens.EventId, RelationalSqlRf3Tokens.EventType, RelationalSqlRf3Tokens.EmptyJson)], ExpectedStreamRevision.NoStream),
        new EnqueueMessage(RelationalSqlRf3Tokens.Queue, RelationalSqlRf3Tokens.MessageId, RelationalSqlRf3Tokens.EmptyJson),
        new AppendSamples(RelationalSqlRf3Tokens.SeriesSet, RelationalSqlRf3Tokens.SeriesId,
            [new(RelationalSqlRf3Tokens.SampleId, RelationalSqlRf3Tokens.SampleAt, RelationalSqlRf3Tokens.SampleValue)]));
    internal SearchRequest Search() => new(Partition, RelationalSqlRf3Tokens.Table, VectorField: RelationalSqlRf3Tokens.VectorField,
        Vector: [RelationalSqlRf3Tokens.UnitVector, RelationalSqlRf3Tokens.ZeroVector], Space: Space);
    internal InspectMessageRequest Inspect(string id = RelationalSqlRf3Tokens.MessageId)
        => new(new(Partition, RelationalSqlRf3Tokens.Queue), id);
    internal ReadStreamRequest ReadStream(string id = RelationalSqlRf3Tokens.StreamId)
        => new(Stream with
        {
            StreamId = id
        });
    private static Task<ResourceDefinition> ConfigureAsync(KeyLoadClient administrator, PartitionRef partition,
        ResourceDefinition definition, CancellationToken cancellationToken)
        => SqlRf3Protocol.SdkAsync<ResourceDefinition>(administrator, SqlRf3Protocol.Call(partition,
            McpCallerTools.ResourcesConfigure, new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition),
            Guid.NewGuid()), cancellationToken);
}

internal sealed record RelationalQ2AdmissionSeed(RelationalSqlRf3Scenario Scenario,
    McpPersistedIdentity Identity, CommandRequest Command, CommitReceipt Receipt)
{
    internal SqlOperationRequest Sql => new(Scenario.Partition,
        RelationalSqlRf3JoinAdmissionSeed.SqlText, AllowFullScan: true,
        QueryDialectVersion: RelationalSqlRf3JoinAdmissionSeed.Dialect);
    internal QueryRequest Query => new(Scenario.Partition,
        RelationalSqlRf3JoinAdmissionSeed.SqlText, AllowFullScan: true,
        QueryDialectVersion: RelationalSqlRf3JoinAdmissionSeed.Dialect);
    internal global::KeyLoad.Query.AstQueryRequest Ast => new(Scenario.Partition,
        new global::KeyLoad.Query.SelectQuery(RelationalSqlRf3Tokens.Table, "l",
            [new("/key", "order_key", "l"), new("/name", "customer_name", "r"),
                new("/title", "customer_id", "l")], null, [new("/key", false)],
            RelationalSqlRf3JoinAdmissionSeed.Limit,
            InnerJoin: new(RelationalSqlRf3JoinAdmissionSeed.Right, "r", "/title", "/id")),
        AllowFullScan: true, AstVersion: RelationalSqlRf3JoinAdmissionSeed.Dialect);
}

internal static class RelationalSqlRf3JoinAdmissionSeed
{
    internal const int Dialect = 2;
    internal const int Limit = 10;
    private const int SecretBytes = 32;
    internal const long Revision = 1;
    internal const string Right = "joincustomers";
    internal const string Order = "cancelled-join-order";
    internal const string Customer = "cancelled-join-customer";
    internal const string LeftJson = "{\"key\":\"cancelled-join-order\",\"title\":\"cancelled-join-customer\",\"count\":1}";
    internal const string RightJson = "{\"id\":\"cancelled-join-customer\",\"name\":\"Ada\"}";
    internal const string SqlText = "SELECT TOP(10) l.[key] AS order_key, r.name AS customer_name, l.title AS customer_id "
        + "FROM agentrows AS l INNER JOIN joincustomers AS r ON l.title = r.id ORDER BY l.[key] ASC";

    internal static async Task<RelationalQ2AdmissionSeed> CreateAsync(KeyLoadClient admin, CancellationToken token)
    {
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(admin, token);
        var right = new ResourceDefinition(Right, ResourceKind.Collection, scenario.Partition.TransactionDomainId)
        {
            RelationalSchema = new(RelationalSqlRf3JoinBudgetData.CustomerIdColumn,
            [new(RelationalSqlRf3JoinBudgetData.CustomerIdColumn, RelationalColumnType.Text),
                new("name", RelationalColumnType.Text)])
        };
        var configured = await SqlRf3Protocol.SdkAsync<ResourceDefinition>(admin,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.ResourcesConfigure,
                new ConfigureResourceRequest(scenario.Partition.TenantId, scenario.Partition.DatabaseId, right),
                Guid.NewGuid()), token);
        await SqlRf3Protocol.EqualAsync(right, configured);
        var identity = await CreateIdentityAsync(admin, scenario.Partition, token);
        var command = scenario.Command(new PutDocument(RelationalSqlRf3Tokens.Table, Order, LeftJson),
            new PutDocument(Right, Customer, RightJson));
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        return new(scenario, identity, command, receipt);
    }

    private static async Task<McpPersistedIdentity> CreateIdentityAsync(KeyLoadClient admin,
        PartitionRef partition, CancellationToken token)
    {
        var id = "c1-probe-q2-" + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var read = Capability.Query | Capability.DocumentsRead;
        var principal = new PrincipalRecord(id, partition.TenantId,
            [new(partition.DatabaseId, RelationalSqlRf3Tokens.Table, read),
                new(partition.DatabaseId, Right, read)], []);
        var keyId = id + "-credential";
        var secret = keyId + "." + Convert.ToHexStringLower(global::System.Security.Cryptography.RandomNumberGenerator.GetBytes(SecretBytes));
        var hash = global::System.Security.Cryptography.SHA256.HashData(global::System.Text.Encoding.UTF8.GetBytes(secret));
        var credential = new ApiKeyRecord(keyId, id, Convert.ToHexStringLower(hash));
        var saved = await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        await SqlRf3Protocol.EqualAsync(principal, saved);
        await Assert.That((await McpCallerAssertions.SdkStatusAsync(
            await admin.ConfigureApiKeyAsync(Guid.NewGuid(), credential, token))).IsSuccess).IsTrue();
        return new(saved, credential, secret);
    }
}
