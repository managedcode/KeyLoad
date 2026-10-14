using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresPureReadRf3Flow
{
    internal static async Task<PostgresPureReadRf3State> WarmAsync(ClusterFixture fixture,
        PostgresRf3Selection selection, KeyLoadClient root, PostgresCompositionRf3Image image, CancellationToken token)
    {
        var state = await PostgresPureReadRf3Provisioning.CreateAsync(root, image, token);
        return await RunOwnedAsync(fixture, state, async (callers, wire) =>
        {
            await PostgresPureReadRf3Assertions.HealthyAsync(callers, wire, state, token);
            await PostgresRf3Assertions.ErrorAsync(wire.ExecuteAsync<QueryPage>(PostgresPureReadRf3Protocol.DocumentSql,
                [], prepared: true, token), PostgresPortalOwnershipProtocol.Validation, ErrorCode.Validation);
            await SqlRf3Protocol.EqualAsync(image, await PostgresCompositionRf3Models.ReadAsync(root, token));
            await PostgresPureReadRf3Assertions.HealthyAsync(callers, wire, state, token);
            var revoked = await PostgresPureReadRf3Provisioning.GraphPolicyAsync(root, state.Principal, allowed: false, token);
            await PostgresPureReadRf3Assertions.DeniedAsync(callers, wire, token);
            await SqlRf3Protocol.EqualAsync(image, await PostgresCompositionRf3Models.ReadAsync(root, token));
            var repaired = await PostgresPureReadRf3Provisioning.GraphPolicyAsync(root, revoked, allowed: true, token);
            state = state with
            {
                Principal = repaired
            };
            await PostgresPureReadRf3Assertions.HealthyAsync(callers, wire, state, token);
            await PostgresPureReadRf3Cancellation.RunAsync(fixture, selection, state, root, token);
            await SqlRf3Protocol.EqualAsync(image, await PostgresCompositionRf3Models.ReadAsync(root, token));
            return state;
        }, token);
    }

    internal static Task<bool> ColdAsync(ClusterFixture fixture, PostgresPureReadRf3State state, CancellationToken token)
        => RunOwnedAsync(fixture, state, async (callers, wire) =>
        {
            await PostgresPureReadRf3Assertions.HealthyAsync(callers, wire, state, token);
            return true;
        }, token);

    private static Task<T> RunOwnedAsync<T>(ClusterFixture fixture, PostgresPureReadRf3State state,
        Func<RequestCqrsRf3Callers, PostgresRf3Caller, Task<T>> operation, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async callers =>
        {
            T? result = default;
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Identity.Principal.Id, state.Identity.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                await ServerFailureObserver.ObserveAsync(async () => { result = await operation(callers, wire); }, failures);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            return result!;
        }, token);
}
