using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class SqlTypedInsertMovementPolicy
{
    internal static async Task<SqlTypedInsertMovementState> RunAsync(TwoRf3MembershipWave wave,
        SqlTypedInsertMovementTls tls, PartitionMovementPublicParentRf3Seed seed,
        SqlTypedInsertMovementState state, CancellationToken token)
    {
        var denied = state.Identity.Principal with
        {
            PolicyEpoch = DeniedEpoch,
            Grants = [new(seed.Partition.DatabaseId, SqlTypedInsertMovementProtocol.Table, Capability.DocumentsRead | Capability.Query)]
        };
        await ConfigureAsync(seed, denied, token);
        await SqlTypedInsertMovementConservation.ObserveAsync(seed,
            () => SqlTypedInsertMovementReplay.RunAsync(wave, tls, state with { PolicyFenced = true }, token), token);
        var repaired = state.Identity.Principal with
        {
            PolicyEpoch = RepairedEpoch
        };
        await ConfigureAsync(seed, repaired, token);
        var next = state with
        {
            Identity = state.Identity with
            {
                Principal = repaired
            },
            PolicyFenced = true
        };
        await SqlTypedInsertMovementConservation.ObserveAsync(seed,
            () => SqlTypedInsertMovementReplay.RunAsync(wave, tls, next, token), token);
        return next;
    }
    private static async Task ConfigureAsync(PartitionMovementPublicParentRf3Seed seed, PrincipalRecord principal, CancellationToken token)
    {
        foreach (var root in new[] { seed.Source, seed.Target })
        {
            await SqlRf3Protocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));
        }
    }
    private const long DeniedEpoch = 2;
    private const long RepairedEpoch = 3;
}

