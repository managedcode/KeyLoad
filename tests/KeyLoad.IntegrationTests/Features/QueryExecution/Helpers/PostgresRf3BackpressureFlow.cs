using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3BackpressureFlow
{
    internal static async Task<PostgresRf3BackpressureState> CreateAndRunAsync(ClusterFixture fixture,
        PostgresRf3Selection selection)
    {
        using var deadline = McpCallerDeadline.Create();
        return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async root =>
        {
            var state = await PostgresRf3BackpressureSeed.CreateAsync(fixture, root.Sdk, deadline.Token);
            var before = await PostgresRf3BackpressureAssertions.ImageAsync(root.Sdk, deadline.Token);
            await PostgresRf3BackpressureTrial.RunAsync(fixture, selection, state, deadline.Token);
            await global::TUnit.Assertions.Assert.That(await PostgresRf3BackpressureAssertions.ImageAsync(root.Sdk, deadline.Token)).IsEqualTo(before);
            await HealthyAsync(fixture, state, deadline.Token);
            await global::TUnit.Assertions.Assert.That(await PostgresRf3BackpressureAssertions.ImageAsync(root.Sdk, deadline.Token)).IsEqualTo(before);
            return state;
        }, deadline.Token);
    }

    internal static async Task HealthyAsync(ClusterFixture fixture, PostgresRf3BackpressureState state, CancellationToken token)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Identity.Secret, async caller =>
        {
            var operation = new SqlOperationRequest(PostgresRf3State.Partition, PostgresRf3BackpressureProtocol.Sql,
                AllowFullScan: true, QueryDialectVersion: SqlOperationProtocol.DefaultQueryDialectVersion);
            await PostgresRf3BackpressureAssertions.PageAsync(state,
                await SqlRf3Protocol.SdkAsync<QueryPage>(caller.Sdk, operation, token));
            await PostgresRf3BackpressureAssertions.PageAsync(state,
                await SqlRf3Protocol.McpAsync<QueryPage>(caller.Mcp, operation, token));
            await PostgresRf3BackpressureAssertions.ReceiptAsync(state,
                await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(state.Command, token)));
            await PostgresRf3BackpressureAssertions.ReceiptAsync(state,
                (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                    await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Command, token))).Value);
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Identity.Principal.Id, state.Identity.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await PostgresRf3BackpressureAssertions.PageAsync(state,
                        await wire.ExecuteAsync<QueryPage>(PostgresRf3BackpressureProtocol.Sql, [], prepared: true, token));
                    await PostgresRf3BackpressureAssertions.ReceiptAsync(state,
                        await wire.CommitAsync(state.Command, prepared: true, token));
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
    }
}
