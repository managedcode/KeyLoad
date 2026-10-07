using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal enum JoinBudgetKind
{
    Work,
    ReadBytes,
    ResultBytes
}

internal sealed record JoinBudgetSeed(PartitionRef Partition, ImmutableArray<EntityRef> Sources,
    long CommitPosition, string ExpectedOrderId, string ExpectedCustomerId, string ExpectedDetail, string ExpectedName);

/// <summary>Builds current typed rows through the actual SDK for the bounded RF3 join cases.</summary>
internal static class RelationalSqlRf3JoinBudgetData
{
    internal const string LeftCollection = "budgetorders";
    internal const string RightCollection = "budgetcustomers";
    internal const string OrderColumn = "order_id";
    internal const string CustomerColumn = "customer_id";
    internal const string DetailColumn = "detail";
    internal const string CustomerIdColumn = "id";
    internal const string NameColumn = "name";
    internal const int DialectVersion = 2;
    private const string Database = "agentdb";
    private const string TransactionDomain = "agent";
    private const string TenantPrefix = "join-budget-";
    private const string LeftAlias = "l";
    private const string RightAlias = "r";
    private const string OrderOutput = "order_id";
    private const string CustomerIdOutput = "customer_id";
    private const string DetailOutput = "left_detail";
    private const string NameOutput = "right_name";
    private const int WorkRows = 3;
    private const int SourcesPerPair = 2;
    private const int InitialSeedIndex = 1;
    private const int ReadPayloadCharacters = 3_000;
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
        + $"ORDER BY {LeftAlias}.{OrderColumn} ASC LIMIT 10";

    internal static PartitionRef Partition(string suffix)
        => new(TenantPrefix + suffix, Database, TransactionDomain, Guid.NewGuid().ToString("N"));

    internal static QueryRequest Query(PartitionRef partition)
        => new(partition, Sql, AllowFullScan: true, QueryDialectVersion: DialectVersion);

    internal static async Task ConfigureAsync(KeyLoadClient sdk, PartitionRef partition, CancellationToken token)
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
        await ConfigureOneAsync(sdk, partition, left, token);
        await ConfigureOneAsync(sdk, partition, right, token);
    }

    internal static async Task<JoinBudgetSeed> SeedAsync(KeyLoadClient sdk, PartitionRef partition,
        JoinBudgetKind kind, bool healthy, CancellationToken token)
    {
        var count = healthy || kind == JoinBudgetKind.ResultBytes ? 1 : WorkRows;
        var references = ImmutableArray.CreateBuilder<EntityRef>(count * SourcesPerPair);
        var position = 0L;
        var expectedOrderId = LeftIdPrefix + (healthy ? HealthyMarker : OverBudgetMarker) + "-" + InitialSeedIndex.ToString(CultureInfo.InvariantCulture);
        var expectedCustomerId = CustomerIdPrefix + (healthy ? HealthyMarker : OverBudgetMarker) + "-" + InitialSeedIndex.ToString(CultureInfo.InvariantCulture);
        var expectedDetail = Payload(kind, healthy);
        var expectedName = Payload(kind, healthy);
        for (var index = InitialSeedIndex; index <= count; index++)
        {
            var orderId = LeftIdPrefix + (healthy ? HealthyMarker : OverBudgetMarker) + "-" + index.ToString(CultureInfo.InvariantCulture);
            var customerId = CustomerIdPrefix + (healthy ? HealthyMarker : OverBudgetMarker) + "-" + index.ToString(CultureInfo.InvariantCulture);
            var detail = Payload(kind, healthy);
            var name = Payload(kind, healthy);
            references.Add(new(partition, LeftCollection, orderId));
            references.Add(new(partition, RightCollection, customerId));
            await PutAsync(sdk, partition, LeftCollection, orderId,
                JsonSerializer.Serialize(new { order_id = orderId, customer_id = customerId, detail }), token);
            position = await PutAsync(sdk, partition, RightCollection, customerId,
                JsonSerializer.Serialize(new { id = customerId, name }), token);
        }
        return new(partition, references.MoveToImmutable(), position, expectedOrderId, expectedCustomerId, expectedDetail, expectedName);
    }

    private static string Payload(JoinBudgetKind kind, bool healthy)
        => healthy || kind == JoinBudgetKind.Work ? SmallValue
            : new string(LargeValue, kind == JoinBudgetKind.ReadBytes ? ReadPayloadCharacters : ResultPayloadCharacters);

    private static async Task ConfigureOneAsync(KeyLoadClient sdk, PartitionRef partition,
        ResourceDefinition resource, CancellationToken token)
    {
        var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, resource);
        var response = await SqlRf3Protocol.SdkAsync<ResourceDefinition>(sdk,
            SqlRf3Protocol.Call(partition, McpCallerTools.ResourcesConfigure, request, Guid.NewGuid()), token);
        await Assert.That(response.Name).IsEqualTo(resource.Name);
    }

    private static async Task<long> PutAsync(KeyLoadClient sdk, PartitionRef partition, string collection,
        string id, string json, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), partition, [new PutDocument(collection, id, json)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token));
        return receipt.Token.Position;
    }
}
