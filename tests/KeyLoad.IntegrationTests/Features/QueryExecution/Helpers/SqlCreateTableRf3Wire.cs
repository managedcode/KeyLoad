using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlCreateTableRf3Wire
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlCreateTableRf3State state, KeyLoadClient root, CancellationToken token)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                state.Identity.Principal.Id, state.Identity.Secret, PostgresRf3Certificate.RootPath(fixture), token);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await RefuseAsync(root, wire, SqlCreateTableRf3Literal.Sql, SqlCreateTableRf3Literal.ValidationState, token);
                await RefuseAsync(root, wire, SqlCreateTableRf3Literal.InvalidIdentity, SqlCreateTableRf3Literal.ValidationState, token);
                await ArmAsync(wire, state.CreatedId, token);
                if (state.EpochFenced)
                { await RefuseAsync(root, wire, SqlCreateTableRf3Literal.Sql, SqlCreateTableRf3Literal.PermissionState, token); }
                else
                { await SqlRf3Protocol.EqualAsync(state.Definition, await wire.ExecuteAsync<ResourceDefinition>(SqlCreateTableRf3Literal.Sql, [], prepared: true, token)); }
                await RefuseAsync(root, wire, SqlCreateTableRf3Literal.Sql, SqlCreateTableRf3Literal.ValidationState, token);
                await ArmAsync(wire, state.FailedId, token);
                await RefuseAsync(root, wire, SqlCreateTableRf3Literal.Sql, state.EpochFenced ? SqlCreateTableRf3Literal.PermissionState : SqlCreateTableRf3Literal.ConflictState, token);
                await ArmAsync(wire, Guid.NewGuid(), token);
                await RefuseAsync(root, wire, SqlCreateTableRf3Literal.ReadSql, SqlCreateTableRf3Literal.ValidationState, token);
                var image = await SqlCreateTableRf3Images.ReadAsync(root, token);
                await ArmAsync(wire, Guid.NewGuid(), token);
                await SqlCreateTableRf3Failures.WireCommandAsync(wire, state.Seed, token);
                await SqlCreateTableRf3Images.RequireAsync(root, image, token);
                if (state.SeedEpoch != state.Identity.Principal.PolicyEpoch)
                { await SqlCreateTableRf3Failures.WireCommandAsync(wire, state.Seed, SqlCreateTableRf3Literal.PermissionState, token); }
                else
                { await SqlRf3Protocol.EqualAsync(state.Receipt, await wire.CommitAsync(state.Seed, prepared: true, token)); }
                await SqlCreateTableRf3Images.RequireAsync(root, image, token);
                if (state.RepairId is { } repairId)
                {
                    await ArmAsync(wire, repairId, token);
                    await SqlRf3Protocol.EqualAsync(SqlCreateTableRf3Literal.Definition(SqlCreateTableRf3Literal.RepairTable),
                        await wire.ExecuteAsync<ResourceDefinition>(SqlCreateTableRf3Literal.RepairSql, [], prepared: true, token));
                }
                await ArmAsync(wire, Guid.NewGuid(), token);
                await RefuseAsync(root, wire, SqlCreateTableRf3Literal.Arm(Guid.NewGuid()), SqlCreateTableRf3Literal.ValidationState, token);
                await RefuseAsync(root, wire, SqlCreateTableRf3Literal.Sql, SqlCreateTableRf3Literal.ValidationState, token);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task ArmAsync(PostgresRf3Caller caller, Guid id, CancellationToken token)
        => await Assert.That(await caller.ExecuteAsync<bool>(SqlCreateTableRf3Literal.Arm(id), [], prepared: true, token)).IsTrue();
    private static async Task RefuseAsync(KeyLoadClient root, PostgresRf3Caller wire, string sql, string state, CancellationToken token)
    {
        var image = await SqlCreateTableRf3Images.ReadAsync(root, token);
        await SqlCreateTableRf3Failures.WireAsync(wire, sql, state, token);
        await SqlCreateTableRf3Images.RequireAsync(root, image, token);
    }
}
