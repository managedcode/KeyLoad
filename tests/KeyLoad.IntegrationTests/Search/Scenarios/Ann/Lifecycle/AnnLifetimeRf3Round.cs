using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class AnnLifetimeRf3Round
{
    private const long NextEpoch = 1;

    internal static async Task<PrincipalRecord> RunAsync(TwoRf3MembershipWave wave, string administrator,
        string secret, RemoteAnnPublicationRf3State state, PrincipalRecord principal, bool wait, bool mcp, CancellationToken token)
    {
        await HealthyAsync(wave, administrator, secret, state, principal.PolicyEpoch, token);
        await using var admin = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            TwoRf3MembershipProtocol.Node1, administrator, token);
        await using var reader = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            TwoRf3MembershipProtocol.Node2, secret, token);
        var changed = principal with
        {
            PolicyEpoch = checked(principal.PolicyEpoch + NextEpoch)
        };
        await AnnLifetimeRf3Hold.RunAsync(wave, admin, reader, state, changed, wait, mcp, cancel: false, token);
        await HealthyAsync(wave, administrator, secret, state, changed.PolicyEpoch, token);
        await AnnLifetimeRf3Hold.RunAsync(wave, admin, reader, state, changed, wait, mcp, cancel: true, token);
        await HealthyAsync(wave, administrator, secret, state, changed.PolicyEpoch, token);
        return changed;
    }

    internal static async Task ConfigureAsync(TwoRf3MembershipWave wave, string administrator,
        PrincipalRecord principal, CancellationToken token)
    {
        await using var admin = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            TwoRf3MembershipProtocol.Node1, administrator, token);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        await RemoteAnnPublicationRf3Assertions.EqualAsync(principal, actual);
    }

    internal static async Task DeniedAsync(TwoRf3MembershipWave wave, string secret,
        RemoteAnnPublicationRf3State state, CancellationToken token)
    {
        await using var reader = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            TwoRf3MembershipProtocol.Node2, secret, token);
        await RemoteAnnPublicationRf3Assertions.RefusedAsync(reader.Sdk, reader.Mcp, state, token);
    }

    internal static async Task HealthyAsync(TwoRf3MembershipWave wave, string administrator, string secret,
        RemoteAnnPublicationRf3State state, long epoch, CancellationToken token)
    {
        await using var admin = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            TwoRf3MembershipProtocol.Node1, administrator, token);
        await using var sourceAdmin = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            TwoRf3MembershipProtocol.Node2, administrator, token);
        await using var owner = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            TwoRf3MembershipProtocol.Node1, secret, token);
        await using var source = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            TwoRf3MembershipProtocol.Node2, secret, token);
        await RemoteAnnPublicationRf3Assertions.ReadsAsync(owner.Sdk, source.Sdk, source.Mcp,
            admin.Sdk, sourceAdmin.Sdk, state, epoch, token);
        await RemoteAnnPublicationRf3Assertions.EqualAsync(state.SeedReceipt,
            await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(state.Seed, token)));
        await RemoteAnnPublicationRf3Assertions.EqualAsync(state.Completed,
            await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.MaintainAnnIndexAsync(state.Original, token)));
        await NativeAnnMaintenanceRf3Assertions.CanonicalAsync(admin.Sdk, admin.Mcp, state.Scenario, token);
    }
}
