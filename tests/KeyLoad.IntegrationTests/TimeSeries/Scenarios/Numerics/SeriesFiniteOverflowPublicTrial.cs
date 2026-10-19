using KeyLoad.IntegrationTests.Features.Authorization;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SeriesFiniteOverflowPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
            {
                var prepared = await SeriesFiniteOverflowPublicPreparation.CreateAsync(fixture, administrator, token);
                return await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3,
                    prepared.Reader.Secret, async reader =>
                    {
                        await SeriesFiniteOverflowPublicChecks.HealthyAsync(administrator, reader, prepared, token);
                        await SeriesFiniteOverflowPublicChecks.OverflowAsync(administrator, reader, prepared, token);
                        await SeriesFiniteOverflowPublicChecks.ReplayAsync(administrator, reader, prepared, token);
                        return prepared;
                    }, token);
            }, token);
        await CredentialAuthorityCold.RestartAsync(fixture, token);
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, administrator => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App,
                McpCallerProtocol.Node3, state.Reader.Secret, async reader =>
                {
                    await SeriesFiniteOverflowPublicChecks.HealthyAsync(administrator, reader, state, token);
                    await SeriesFiniteOverflowPublicChecks.OverflowAsync(administrator, reader, state, token);
                    await SeriesFiniteOverflowPublicChecks.ReplayAsync(administrator, reader, state, token);
                    var receipt = await McpCallerAssertions.SdkSuccessAsync(
                        await administrator.Sdk.CommitAsync(state.Expiry, token));
                    await SdkProblemBudgetPublicAssertions.ReceiptAsync(receipt, state.Expiry, [new("expireSamples", TimeSeriesClusterScenario.Set, SeriesFiniteOverflowPublicExpected.Series, 1L)]);
                    state.ExpiryReceipt = receipt;
                    state.Purged = true;
                    state.CurrentOutbox = await McpCallerAssertions.SdkSuccessAsync(
                        await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
                    await SeriesFiniteOverflowPublicChecks.CurrentAsync(administrator, state, token);
                    await SeriesFiniteOverflowPublicChecks.HealthyAsync(administrator, reader, state, token);
                    await SeriesFiniteOverflowPublicChecks.ReplayAsync(administrator, reader, state, token);
                    return true;
                }, token), token);
        await CredentialAuthorityCold.RestartAsync(fixture, token);
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, administrator => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App,
                McpCallerProtocol.Node3, state.Reader.Secret, async reader =>
                {
                    await SeriesFiniteOverflowPublicChecks.CurrentAsync(administrator, state, token);
                    await SeriesFiniteOverflowPublicChecks.HealthyAsync(administrator, reader, state, token);
                    await SeriesFiniteOverflowPublicChecks.ReplayAsync(administrator, reader, state, token);
                    return true;
                }, token), token);
    }
}
