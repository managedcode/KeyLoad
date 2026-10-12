using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class DeclarativeCompositionBudgetFlow
{
    private const string Queue = "bounded-actions";
    private const string Graph = "bounded-knowledge";
    private const string First = "a";
    private const string Second = "b";
    private const string Prefix = "bounded-";
    private const int RefusedMaximum = 1;
    private const int ExactMaximum = 2;
    private const long InitialRevision = 1;

    internal static async Task<DeclarativeCompositionBudgetState> RunAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RelationalSqlRf3Scenario scenario, QueueGraphLink link, CancellationToken token)
    {
        foreach (var (name, kind) in new[] { (Queue, ResourceKind.WorkQueue), (Graph, ResourceKind.Graph) })
        {
            await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(scenario.Partition.TenantId, scenario.Partition.DatabaseId,
                    new(name, kind, scenario.Partition.TransactionDomainId)), token));
        }
        var body = JsonSerializer.Serialize(link, JsonDefaults.Options);
        await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(scenario.Command(
            new EnqueueMessage(Queue, First, body), new EnqueueMessage(Queue, Second, body)), token));
        var beforeFirst = await MessageAsync(sdk, scenario.Partition, First, token);
        var beforeSecond = await MessageAsync(sdk, scenario.Partition, Second, token);
        var limitedMutation = new QueueToGraph(Graph, Queue, Prefix, RefusedMaximum);
        var limited = scenario.Command(limitedMutation);
        var sql = DeclarativeCompositionSql.Forward(limited, limitedMutation);
        foreach (var refusedSql in new[] { sql, sql })
        {
            var refused = await sdk.ExecuteSqlAsync(refusedSql, token);
            await Assert.That(refused.IsFailed).IsTrue();
            await Assert.That(refused.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.BudgetExceeded));
            await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SqlOperationProtocol.ToolName, refusedSql, token),
                ErrorCode.BudgetExceeded, dispatched: true);
            await Assert.That((await GraphAsync(sdk, scenario, token)).Edges).IsEmpty();
            await SqlRf3Protocol.EqualAsync(beforeFirst, await MessageAsync(sdk, scenario.Partition, First, token));
            await SqlRf3Protocol.EqualAsync(beforeSecond, await MessageAsync(sdk, scenario.Partition, Second, token));
        }
        var exactMutation = limitedMutation with { MaxMessages = ExactMaximum };
        var exact = scenario.Command(exactMutation);
        var healthySql = DeclarativeCompositionSql.Forward(exact, exactMutation);
        var receipt = await SqlRf3Protocol.SdkAsync<CommitReceipt>(sdk, healthySql, token);
        await Assert.That(receipt.CommandId).IsEqualTo(exact.CommandId);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(ExactMaximum);
        await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(mcp, healthySql, token));
        await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(exact, token)));
        var graph = await GraphAsync(sdk, scenario, token);
        await Assert.That(graph.Edges.Select(edge => edge.Id).SequenceEqual(new[] { Prefix + First, Prefix + Second })).IsTrue();
        foreach (var edge in graph.Edges)
        {
            await Assert.That(edge.From).IsEqualTo(link.From);
            await Assert.That(edge.To).IsEqualTo(link.To);
            await Assert.That(edge.Label).IsEqualTo(link.Label);
            await Assert.That(edge.AttributesJson).IsEqualTo(link.AttributesJson);
            await Assert.That(edge.Revision).IsEqualTo(InitialRevision);
        }
        await SqlRf3Protocol.EqualAsync(beforeFirst, await MessageAsync(sdk, scenario.Partition, First, token));
        await SqlRf3Protocol.EqualAsync(beforeSecond, await MessageAsync(sdk, scenario.Partition, Second, token));
        return new(limited, exact, receipt, beforeFirst, beforeSecond, graph);
    }

    internal static async Task ColdAsync(global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers callers, RelationalSqlRf3Scenario scenario,
        DeclarativeCompositionBudgetState state, CancellationToken token)
    {
        var exact = (QueueToGraph)state.Command.Mutations[0];
        var sql = DeclarativeCompositionSql.Forward(state.Command, exact);
        await SqlRf3Protocol.EqualAsync(state.Receipt, await SqlRf3Protocol.SdkAsync<CommitReceipt>(callers.Sdk, sql, token));
        await SqlRf3Protocol.EqualAsync(state.Receipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(callers.Mcp, sql, token));
        await SqlRf3Protocol.EqualAsync(state.Graph, await GraphAsync(callers.Sdk, scenario, token));
        await SqlRf3Protocol.EqualAsync(state.First, await MessageAsync(callers.Sdk, scenario.Partition, First, token));
        await SqlRf3Protocol.EqualAsync(state.Second, await MessageAsync(callers.Sdk, scenario.Partition, Second, token));
        var limited = DeclarativeCompositionSql.Forward(state.Refused, (QueueToGraph)state.Refused.Mutations[0]);
        var refused = await callers.Sdk.ExecuteSqlAsync(limited, token);
        await Assert.That(refused.IsFailed).IsTrue();
        await Assert.That(refused.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.BudgetExceeded));
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, limited, token),
            ErrorCode.BudgetExceeded, dispatched: true);
        await SqlRf3Protocol.EqualAsync(state.Graph, await GraphAsync(callers.Sdk, scenario, token));
        await SqlRf3Protocol.EqualAsync(state.First, await MessageAsync(callers.Sdk, scenario.Partition, First, token));
        await SqlRf3Protocol.EqualAsync(state.Second, await MessageAsync(callers.Sdk, scenario.Partition, Second, token));
    }

    private static Task<global::KeyLoad.GraphTraversal> GraphAsync(KeyLoadClient sdk,
        RelationalSqlRf3Scenario scenario, CancellationToken token)
        => ReadGraphAsync(sdk, new(scenario.Partition, Graph, scenario.First), token);

    private static async Task<global::KeyLoad.GraphTraversal> ReadGraphAsync(KeyLoadClient sdk,
        TraverseRequest request, CancellationToken token)
        => await McpCallerAssertions.SdkSuccessAsync(await sdk.TraverseAsync(request, token));

    private static async Task<MessageInspection> MessageAsync(KeyLoadClient sdk, PartitionRef partition,
        string id, CancellationToken token)
    {
        var message = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(new(new(partition, Queue), id), token));
        await Assert.That(message).IsNotNull();
        await Assert.That(message!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(message.Metadata.Attempts).IsEqualTo(RelationalSqlRf3Tokens.NoResults);
        await Assert.That(message.Metadata.LeaseOwner).IsNull();
        return message;
    }
}
