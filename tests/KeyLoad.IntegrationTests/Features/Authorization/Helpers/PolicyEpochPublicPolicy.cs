using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class PolicyEpochPublicPolicy
{
    internal static async Task<McpPersistedIdentity> ChangeAsync(KeyLoadClient root, McpPersistedIdentity identity,
        ImmutableArray<string> fields, bool revoked, CancellationToken token)
    {
        var principal = identity.Principal with
        { FieldGrants = fields, Revoked = revoked, PolicyEpoch = checked(identity.Principal.PolicyEpoch + 1) };
        var actual = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        await SqlRf3Protocol.EqualAsync(principal, actual);
        return identity with { Principal = principal };
    }

    internal static async Task ReplayAsync(KeyLoadClient root, PolicyEpochPublicState state, CancellationToken token)
    {
        await PolicyEpochPublicAssertions.ImageAsync(root, state.Scenario, token);
        var replay = await McpCallerAssertions.SdkSuccessAsync(await root.CommitAsync(state.Seed, token));
        await SqlRf3Protocol.EqualAsync(state.Receipt, replay);
        await PolicyEpochPublicAssertions.ImageAsync(root, state.Scenario, token);
    }
}
