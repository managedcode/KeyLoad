using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class DeclarativeReverseQuotaFlow
{
    internal const string Target = "reverse-quota";
    internal const string Graph = "bounded-knowledge";
    internal const string Prefix = "quota-";
    private const long FixedQuota = 2;
    private const string MissingSeed = "The original literal composition source document is unavailable.";

    internal static async Task<DeclarativeReverseQuotaState> SeedAndRefuseAsync(
        RequestCqrsClusterCallers callers, CancellationToken token)
    {
        var scenario = await RelationalSqlClusterScenario.CreateAsync(callers.Sdk, token);
        var link = new QueueGraphLink(scenario.First, scenario.Second, RelationalSqlClusterTokens.EdgeLabel);
        var seed = scenario.Command(
            new PutDocument(RelationalSqlClusterTokens.Table, RelationalSqlClusterTokens.FirstId, RelationalSqlClusterTokens.FirstRow),
            new PutDocument(RelationalSqlClusterTokens.Table, RelationalSqlClusterTokens.SecondId, RelationalSqlClusterTokens.SecondRow));
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(seed, token));
        await Assert.That(receipt.CommandId).IsEqualTo(seed.CommandId);
        var first = await PublicReadAssertions.DocumentAsync(await callers.Sdk.GetAsync(scenario.First, token))
            ?? throw new InvalidOperationException(MissingSeed);
        var second = await PublicReadAssertions.DocumentAsync(await callers.Sdk.GetAsync(scenario.Second, token))
            ?? throw new InvalidOperationException(MissingSeed);
        var source = await DeclarativeCompositionBudgetFlow.RunAsync(callers.Sdk, callers.Mcp, scenario, link, token);
        var expected = new ResourceDefinition(Target, ResourceKind.WorkQueue, scenario.Partition.TransactionDomainId)
        {
            QueuePolicy = new() { MaxStoredMessages = FixedQuota }
        };
        var configureId = Guid.NewGuid();
        var actual = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(configureId,
            new(scenario.Partition.TenantId, scenario.Partition.DatabaseId, expected), token));
        await SqlClusterProtocol.EqualAsync(expected, actual);
        var holder = scenario.Command(new EnqueueMessage(Target, DeclarativeReverseQuotaTrial.HolderId,
            DeclarativeReverseQuotaTrial.Payload));
        var holderReceipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(holder, token));
        await DeclarativeReverseQuotaTrial.ReceiptAsync(holder, holderReceipt, "enqueue", 1L);
        var refused = scenario.Command(new GraphToQueueMutation(Target, Graph, scenario.First, Prefix));
        var problem = await RefusedAsync(callers, refused, token);
        var state = new DeclarativeReverseQuotaState(scenario, source, actual, seed, receipt, first, second, refused, problem, holder, holderReceipt, configureId);
        await OriginalAsync(callers, state, token);
        return state;
    }

    internal static async Task<DeclarativeReverseQuotaState> RepairAndHealthyAsync(
        RequestCqrsClusterCallers callers, DeclarativeReverseQuotaState state, CancellationToken token)
    {
        await OriginalAsync(callers, state, token);
        state = await DeclarativeReverseQuotaTrial.ReleaseAsync(callers, state, token);
        await OriginalAsync(callers, state, token);
        var healthy = state.Refused with
        {
            CommandId = Guid.NewGuid()
        };
        var sql = DeclarativeCompositionSql.Reverse(healthy, (GraphToQueueMutation)healthy.Mutations[0]);
        var receipt = await SqlClusterProtocol.McpAsync<CommitReceipt>(callers.Mcp, sql, token);
        state = state with
        {
            Healthy = healthy,
            Receipt = receipt
        };
        await OriginalAsync(callers, state, token);
        return state;
    }

    internal static async Task OriginalAsync(RequestCqrsClusterCallers callers,
        DeclarativeReverseQuotaState state, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(state.Problem, await RefusedAsync(callers, state.Refused, token));
        await DeclarativeReverseQuotaAssertions.ModelsAsync(callers, state, token);
        await DeclarativeReverseQuotaTrial.HistoryAsync(callers, state, token);
        if (state.Healthy is not { } healthy || state.Receipt is not { } receipt)
        {
            return;
        }
        await DeclarativeReverseQuotaAssertions.ReceiptAsync(healthy, receipt);
        var sql = DeclarativeCompositionSql.Reverse(healthy, (GraphToQueueMutation)healthy.Mutations[0]);
        await SqlClusterProtocol.EqualAsync(receipt, await SqlClusterProtocol.SdkAsync<CommitReceipt>(callers.Sdk, sql, token));
        await SqlClusterProtocol.EqualAsync(receipt, await SqlClusterProtocol.McpAsync<CommitReceipt>(callers.Mcp, sql, token));
        await SqlClusterProtocol.EqualAsync(receipt,
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(healthy, token)));
        await DeclarativeReverseQuotaAssertions.ModelsAsync(callers, state, token);
    }

    private static async Task<JsonElement> RefusedAsync(RequestCqrsClusterCallers callers,
        CommandRequest command, CancellationToken token)
    {
        var sql = DeclarativeCompositionSql.Reverse(command, (GraphToQueueMutation)command.Mutations[0]);
        var actual = await callers.Sdk.ExecuteSqlAsync(sql, token);
        await Assert.That(actual.IsFailed).IsTrue();
        await Assert.That(actual.Value.ValueKind).IsEqualTo(global::System.Text.Json.JsonValueKind.Undefined);
        await Assert.That(actual.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.ResourceExhausted));
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token),
            ErrorCode.ResourceExhausted, dispatched: true);
        return JsonSerializer.SerializeToElement(actual.Problem, JsonDefaults.Options);
    }
}
