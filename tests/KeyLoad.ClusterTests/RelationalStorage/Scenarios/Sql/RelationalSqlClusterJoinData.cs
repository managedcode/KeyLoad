using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.ClusterTests.ResourceExecution;

namespace KeyLoad.ClusterTests.RelationalStorage;

/// <summary>Builds current typed rows through the actual SDK for the bounded Orleans join cases.</summary>
internal static class RelationalSqlClusterJoinData
{
    internal const string LeftCollection = "budgetorders";
    internal const string RightCollection = "budgetcustomers";
    internal const string OrderColumn = "order_id";
    internal const string CustomerColumn = "customer_id";
    internal const string DetailColumn = "detail";
    internal const string CustomerIdColumn = "id";
    internal const string NameColumn = "name";
    internal const int DialectVersion = SqlOperationProtocol.DefaultQueryDialectVersion;
    private const string Database = "agentdb";
    private const string TransactionDomain = "agent";
    private const string TenantPrefix = "join-budget-";
    private const string LeftAlias = "l";
    private const string RightAlias = "r";
    private const string OrderOutput = "order_id";
    private const string CustomerIdOutput = "customer_id";
    private const string DetailOutput = "left_detail";
    private const string NameOutput = "right_name";
    private const int SeededPairs = 1;
    private const int SourcesPerPair = 2;
    private const int ResultPayloadCharacters = 2_500;
    private const string SmallValue = "small";
    private const char LargeValue = 'x';
    private const string LeftIdPrefix = "order-";
    private const string CustomerIdPrefix = "customer-";
    private const string HealthyMarker = "healthy";
    private const string OverBudgetMarker = "over-budget";

    internal static string Sql => $"SELECT {LeftAlias}.{OrderColumn} AS {OrderOutput}, {LeftAlias}.{DetailColumn} AS {DetailOutput}, "
        + $"{RightAlias}.{NameColumn} AS {NameOutput}, {RightAlias}.{CustomerIdColumn} AS {CustomerIdOutput} "
        + $"FROM {LeftCollection} AS {LeftAlias} "
        + $"INNER JOIN {RightCollection} AS {RightAlias} ON {LeftAlias}.{CustomerColumn} = {RightAlias}.{CustomerIdColumn} "
        + $"ORDER BY {LeftAlias}.{OrderColumn} ASC OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY";

    internal static PartitionRef Partition(string suffix)
        => new(TenantPrefix + suffix, Database, TransactionDomain, Guid.NewGuid().ToString("N"));

    internal static QueryRequest Query(PartitionRef partition)
        => new(partition, Sql, AllowFullScan: true, QueryDialectVersion: DialectVersion);

    internal static async Task<(EntityRefLifetime Left, EntityRefLifetime Right)> ConfigureAsync(KeyLoadClient sdk,
        PartitionRef partition, CancellationToken token)
    {
        var left = new ResourceDefinition(LeftCollection, ResourceKind.Collection, partition.TransactionDomainId)
        {
            RelationalSchema = new(OrderColumn, [new(OrderColumn, RelationalColumnType.Text),
                new(CustomerColumn, RelationalColumnType.Text), new(DetailColumn, RelationalColumnType.Text)])
        };
        var right = new ResourceDefinition(RightCollection, ResourceKind.Collection, partition.TransactionDomainId)
        {
            RelationalSchema = new(CustomerIdColumn, [new(CustomerIdColumn, RelationalColumnType.Text),
                new(NameColumn, RelationalColumnType.Text)])
        };
        var leftLifetime = await ConfigureOneAsync(sdk, partition, left, token);
        var rightLifetime = await ConfigureOneAsync(sdk, partition, right, token);
        return (leftLifetime, rightLifetime);
    }

    internal static async Task<JoinBudgetSeed> SeedAsync(KeyLoadClient sdk, PartitionRef partition,
        (EntityRefLifetime Left, EntityRefLifetime Right) lifetimes, bool healthy, CancellationToken token)
    {
        var count = SeededPairs;
        var references = ImmutableArray.CreateBuilder<EntityRef>(count * SourcesPerPair);
        var stored = ImmutableArray.CreateBuilder<EntityRef>(count * SourcesPerPair);
        var position = 0L;
        var expectedOrderId = LeftIdPrefix + (healthy ? HealthyMarker : OverBudgetMarker) + "-" + 1.ToString(CultureInfo.InvariantCulture);
        var expectedCustomerId = CustomerIdPrefix + (healthy ? HealthyMarker : OverBudgetMarker) + "-" + 1.ToString(CultureInfo.InvariantCulture);
        var expectedDetail = Payload(healthy);
        var expectedName = Payload(healthy);
        for (var index = 1; index <= count; index++)
        {
            var orderId = LeftIdPrefix + (healthy ? HealthyMarker : OverBudgetMarker) + "-" + index.ToString(CultureInfo.InvariantCulture);
            var customerId = CustomerIdPrefix + (healthy ? HealthyMarker : OverBudgetMarker) + "-" + index.ToString(CultureInfo.InvariantCulture);
            var detail = Payload(healthy);
            var name = Payload(healthy);
            references.Add(new(partition, LeftCollection, orderId));
            references.Add(new(partition, RightCollection, customerId));
            stored.Add(new(partition, LeftCollection, orderId) { Lifetime = lifetimes.Left });
            stored.Add(new(partition, RightCollection, customerId) { Lifetime = lifetimes.Right });
            await PutAsync(sdk, partition, LeftCollection, orderId,
                JsonSerializer.Serialize(new
                {
                    order_id = orderId,
                    customer_id = customerId,
                    detail
                }), token);
            position = await PutAsync(sdk, partition, RightCollection, customerId,
                JsonSerializer.Serialize(new
                {
                    id = customerId,
                    name
                }), token);
        }
        return new(partition, references.MoveToImmutable(), stored.MoveToImmutable(), position, expectedOrderId,
            expectedCustomerId, expectedDetail, expectedName);
    }

    private static string Payload(bool healthy)
        => healthy ? SmallValue : new string(LargeValue, ResultPayloadCharacters);

    private static async Task<EntityRefLifetime> ConfigureOneAsync(KeyLoadClient sdk, PartitionRef partition,
        ResourceDefinition resource, CancellationToken token)
    {
        var commandId = Guid.NewGuid();
        var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, resource);
        var response = await SqlClusterProtocol.SdkAsync<ResourceDefinition>(sdk,
            SqlClusterProtocol.Call(partition, McpCallerTools.ResourcesConfigure, request, commandId), token);
        await Assert.That(response.Name).IsEqualTo(resource.Name);
        return ResourceCreationLifetime.Create(commandId, request);
    }

    private static async Task<long> PutAsync(KeyLoadClient sdk, PartitionRef partition, string collection,
        string id, string json, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), partition, [new PutDocument(collection, id, json)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token));
        return receipt.Token.Position;
    }
}
