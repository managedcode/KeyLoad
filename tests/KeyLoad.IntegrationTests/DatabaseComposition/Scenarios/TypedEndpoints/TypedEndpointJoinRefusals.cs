using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class TypedEndpointJoinRefusals
{
    private const string MissingCollection = "join-unconfigured";
    private const string ChangedPrefix = "changed-";
    private const string RollbackPrefix = "rollback-";
    private const string RollbackId = "rollback-marker";
    private const string RollbackJson = "{\"id\":\"rollback-marker\",\"title\":\"rolled-back\"}";
    private const int BelowSourceCount = 2;

    internal static async Task RunAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers callers,
        TypedEndpointJoinState state, CancellationToken token)
    {
        var partition = state.Seed.Partition;
        var ordinary = new CommandRequest(Guid.NewGuid(), partition,
            [new QueueToGraph(TypedEndpointJoinProtocol.Graph, TypedEndpointJoinProtocol.Queue, ChangedPrefix)]);
        await RefusedAsync(admin, ordinary, ErrorCode.NotFound, sql: false, token);
        await UnchangedAsync(admin, state, token);
        var oneNull = new CommandRequest(Guid.NewGuid(), partition,
            [TypedEndpointJoinProtocol.Mutation() with { ToCollection = null }]);
        await RefusedAsync(admin, oneNull, ErrorCode.Validation, sql: false, token);
        await UnchangedAsync(admin, state, token);
        var undeclared = new CommandRequest(Guid.NewGuid(), partition,
            [TypedEndpointJoinProtocol.Mutation() with { ToCollection = MissingCollection }]);
        await RefusedAsync(admin, undeclared, ErrorCode.NotFound, sql: true, token);
        await UnchangedAsync(admin, state, token);
        var changed = state.Healthy with
        {
            Mutations = [TypedEndpointJoinProtocol.Mutation(ChangedPrefix)]
        };
        await RefusedAsync(callers, changed, ErrorCode.Conflict, sql: true, token);
        await UnchangedAsync(admin, state, token);
        await TypedEndpointJoinAssertions.ReplayAsync(callers, state.Healthy, state.Receipt, token);
        var bounded = new CommandRequest(Guid.NewGuid(), partition,
            [TypedEndpointJoinProtocol.Mutation(ChangedPrefix, BelowSourceCount)]);
        await RefusedAsync(callers, bounded, ErrorCode.BudgetExceeded, sql: true, token);
        await RefusedAsync(callers, bounded, ErrorCode.BudgetExceeded, sql: true, token);
        await UnchangedAsync(admin, state, token);
        await RollbackAsync(admin, state, token);
    }

    private static async Task RollbackAsync(RequestCqrsClusterCallers admin,
        TypedEndpointJoinState state, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Seed.Partition,
        [
            new PutDocument(TypedEndpointJoinProtocol.FromCollection, RollbackId, RollbackJson),
            TypedEndpointJoinProtocol.Mutation(RollbackPrefix),
            TypedEndpointJoinProtocol.Mutation(RollbackPrefix)
        ]);
        await RefusedAsync(admin, command, ErrorCode.RevisionConflict, sql: false, token);
        await Assert.That(await PublicReadAssertions.DocumentAsync(await admin.Sdk.GetAsync(
            new(state.Seed.Partition, TypedEndpointJoinProtocol.FromCollection, RollbackId), token))).IsNull();
        await UnchangedAsync(admin, state, token);
        await RefusedAsync(admin, command, ErrorCode.RevisionConflict, sql: false, token);
        await UnchangedAsync(admin, state, token);
    }

    private static async Task RefusedAsync(RequestCqrsClusterCallers callers, CommandRequest command,
        ErrorCode expected, bool sql, CancellationToken token)
    {
        if (sql)
        {
            var request = TypedEndpointJoinProtocol.Sql(command);
            var result = await callers.Sdk.ExecuteSqlAsync(request, token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value).IsNull();
            await Assert.That(result.Problem!.ErrorCode).IsEqualTo(expected.ToString());
            await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, request, token),
                expected, dispatched: true);
        }
        else
        {
            var result = await callers.Sdk.CommitAsync(command, token);
            await Assert.That(result.IsFailed).IsTrue();
            await Assert.That(result.Value).IsNull();
            await Assert.That(result.Problem!.ErrorCode).IsEqualTo(expected.ToString());
            await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token),
                expected, dispatched: true);
        }
    }

    private static async Task UnchangedAsync(RequestCqrsClusterCallers admin, TypedEndpointJoinState state, CancellationToken token)
        => await SqlClusterProtocol.EqualAsync(state.Models,
            await TypedEndpointJoinAssertions.CaptureAsync(admin, state.Seed.Partition, token));
}
