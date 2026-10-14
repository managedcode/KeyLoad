using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresClosedCommandRf3Operations
{
    internal static async Task RefusedAsync(ClusterFixture fixture, PostgresClosedCommandRf3State state,
        KeyLoadClient root, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                state.Admin.Principal.Id, state.Admin.Secret, PostgresRf3Certificate.RootPath(fixture), token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                foreach (var prepared in new[] { false, true })
                {
                    await PostgresRf3Assertions.ErrorAsync(wire.ExecuteAsync<PrincipalRecord>(
                        PostgresClosedCommandRf3Protocol.Sql, PostgresClosedCommandRf3Protocol.Parameters(state), prepared, token),
                        PostgresClosedCommandRf3Protocol.UnsupportedState, ErrorCode.UnsupportedCapability);
                    await PostgresClosedCommandRf3Assertions.ModelsAsync(root, state, token);
                    await PostgresClosedCommandRf3Assertions.NativeHealthyAsync(wire, state, prepared, token);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers admin,
        PostgresClosedCommandRf3State state, Func<Task> afterResult, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(afterResult);
        await SqlRf3Protocol.EqualAsync(state.Denied,
            await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigurePrincipalAsync(state.CommandId, state.Denied, token)));
        await afterResult();
        var operation = PostgresClosedCommandRf3Protocol.Operation(state);
        await SqlRf3Protocol.EqualAsync(state.Denied, await SqlRf3Protocol.SdkAsync<PrincipalRecord>(admin.Sdk, operation, token));
        await afterResult();
        await SqlRf3Protocol.EqualAsync(state.Denied, await SqlRf3Protocol.McpAsync<PrincipalRecord>(admin.Mcp, operation, token));
        await afterResult();
    }

    internal static Task<T> OwnedAsync<T>(ClusterFixture fixture, PostgresClosedCommandRf3State state,
        Func<RequestCqrsRf3Callers, Task<T>> operation, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, state.Admin.Secret, operation, token);
}
