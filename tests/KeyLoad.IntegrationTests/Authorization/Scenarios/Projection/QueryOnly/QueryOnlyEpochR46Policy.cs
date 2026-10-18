using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class QueryOnlyEpochR46Policy
{
    internal static async Task<McpPersistedIdentity> ChangeAsync(KeyLoadClient administrator,
        McpPersistedIdentity current, PrincipalRecord baseline, bool allowed, CancellationToken token)
    {
        await Assert.That(current.Principal.Id).IsEqualTo(baseline.Id);
        await Assert.That(current.Principal.ClusterAdministrator).IsFalse();
        await Assert.That(current.Principal.Revoked).IsFalse();
        await Assert.That(baseline.Grants.Any(grant =>
            (grant.Capabilities & Capability.Query) != 0)).IsTrue();
        var grants = allowed ? baseline.Grants : baseline.Grants.Select(grant => grant with
        {
            Capabilities = grant.Capabilities & ~Capability.Query
        }).ToImmutableArray();
        var expected = baseline with
        {
            Grants = grants,
            PolicyEpoch = checked(current.Principal.PolicyEpoch + 1)
        };
        await Assert.That(expected.Grants.Any(grant =>
            (grant.Capabilities & Capability.DocumentsRead) != 0)).IsTrue();
        var persisted = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await SqlClusterProtocol.EqualAsync(expected, persisted);
        return current with
        {
            Principal = persisted
        };
    }
}
