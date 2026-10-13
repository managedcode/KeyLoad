using KeyLoad.IntegrationTests.Features.Authorization;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ScopedSampleIdentityPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
            {
                var created = await ScopedSampleIdentityPublicSeed.CreateAsync(fixture, administrator, token);
                return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3,
                    created.Reader.Secret, async reader =>
                    {
                        await VerifyAsync(administrator, reader, created, token);
                        return created;
                    }, token);
            }, token);
        await CredentialAuthorityCold.RestartAsync(fixture, token);
        await ColdStageAsync(fixture, state, token);
        await CredentialAuthorityCold.RestartAsync(fixture, token);
        await ColdStageAsync(fixture, state, token);
    }

    private static Task<bool> ColdStageAsync(ClusterFixture fixture, ScopedSampleIdentityPublicState state,
        CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            administrator => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3,
                state.Reader.Secret, async reader =>
                {
                    await VerifyAsync(administrator, reader, state, token);
                    return true;
                }, token), token);

    private static async Task VerifyAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, ScopedSampleIdentityPublicState state, CancellationToken token)
    {
        await ScopedSampleIdentityPublicChecks.CurrentAsync(administrator, state, token);
        await ScopedSampleIdentityPublicCallerReads.ReadAsync(administrator, reader, state, token);
        await ScopedSampleIdentityPublicChecks.HealthyAsync(administrator, reader, state, token);
        await ScopedSampleIdentityPublicChecks.ConflictsAsync(administrator, reader, state, token);
        await ScopedSampleIdentityPublicObservation.ReplayAsync(administrator, reader, state, token);
    }
}
