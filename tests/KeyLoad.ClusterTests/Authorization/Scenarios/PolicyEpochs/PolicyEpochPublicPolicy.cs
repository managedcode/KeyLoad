using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Authorization;

internal static class PolicyEpochPublicPolicy
{
    internal static async Task<McpPersistedIdentity> ChangeAsync(KeyLoadClient root, McpPersistedIdentity identity,
        ImmutableArray<string> fields, bool revoked, CancellationToken token)
    {
        var principal = identity.Principal with
        {
            FieldGrants = fields,
            Revoked = revoked,
            PolicyEpoch = checked(identity.Principal.PolicyEpoch + 1)
        };
        var actual = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        await SqlClusterProtocol.EqualAsync(principal, actual);
        return identity with
        {
            Principal = principal
        };
    }

    internal static async Task ReplayAsync(KeyLoadClient root, PolicyEpochPublicState state, CancellationToken token)
    {
        await PolicyEpochPublicAssertions.ImageAsync(root, state.Scenario, token);
        var replay = await McpCallerAssertions.SdkSuccessAsync(await root.CommitAsync(state.Seed, token));
        await SqlClusterProtocol.EqualAsync(state.Receipt, replay);
        await PolicyEpochPublicAssertions.ImageAsync(root, state.Scenario, token);
    }
}
