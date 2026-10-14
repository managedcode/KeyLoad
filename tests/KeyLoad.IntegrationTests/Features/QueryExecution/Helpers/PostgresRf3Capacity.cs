using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using Npgsql;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3Capacity
{
    internal static async Task RunAsync(ClusterFixture fixture, PostgresRf3State state,
        KeyLoadClient root, CancellationToken token)
    {
        var image = await PostgresRf3Assertions.ImageAsync(root, token);
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                state.Reader.Principal.Id, state.Reader.Secret, PostgresRf3Certificate.RootPath(fixture), token);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var commands = new List<NpgsqlCommand>();
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    for (var index = PostgresRf3Protocol.FirstColumn; index < StatementGrant; index++)
                    {
                        var command = PostgresRf3Commands.Plan(state.Sql, index, wire.Connection);
                        commands.Add(command);
                        await command.PrepareAsync(token);
                    }
                    await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                    {
                        await using var ninth = PostgresRf3Commands.RefusedPlan(state.Sql, wire.Connection);
                        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                        {
                            await PostgresRf3Assertions.ErrorAsync(ninth.PrepareAsync(token),
                                ResourceSqlState, ErrorCode.ResourceExhausted);
                        }, failures);
                    }, failures);
                    if (failures.Count != PostgresRf3Protocol.FirstColumn)
                    {
                        return;
                    }
                    await global::TUnit.Assertions.Assert.That(await PostgresRf3Assertions.ImageAsync(root, token)).IsEqualTo(image);
                    foreach (var command in commands)
                    {
                        await command.UnprepareAsync(token);
                    }
                    await PostgresRf3Assertions.PageAsync(state,
                        await wire.ExecuteAsync<QueryPage>(state.Sql, [], prepared: true, token));
                    await global::TUnit.Assertions.Assert.That(await PostgresRf3Assertions.ImageAsync(root, token)).IsEqualTo(image);
                }, failures);
                foreach (var command in commands)
                {
                    await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                        () => command.DisposeAsync().AsTask(), failures);
                }
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private const int StatementGrant = 8;
    private const string ResourceSqlState = "54000";
}
