using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class NativeRemoteGraphAuthorizationPolicy
{
    internal static async Task<PrincipalRecord> RevokeWriteAsync(KeyLoadClient administrator,
        PrincipalRecord original, CancellationToken cancellationToken)
    {
        await Assert.That(original.ClusterAdministrator).IsFalse();
        await Assert.That(original.Revoked).IsFalse();
        await Assert.That(original.Grants.IsDefault).IsFalse();
        await Assert.That(original.Grants.Any(grant => (grant.Capabilities & Capability.GraphWrite) != 0)).IsTrue();
        var denied = original with
        {
            PolicyEpoch = checked(original.PolicyEpoch + 1),
            Grants = original.Grants.Select(grant => grant with
            { Capabilities = grant.Capabilities & ~Capability.GraphWrite }).ToImmutableArray()
        };
        return await PersistAsync(administrator, denied, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<PrincipalRecord> RestoreWriteAsync(KeyLoadClient administrator,
        PrincipalRecord original, PrincipalRecord denied, CancellationToken cancellationToken)
    {
        await Assert.That(denied.Id).IsEqualTo(original.Id);
        await Assert.That(denied.PolicyEpoch).IsEqualTo(checked(original.PolicyEpoch + 1));
        var restored = original with { PolicyEpoch = checked(denied.PolicyEpoch + 1) };
        return await PersistAsync(administrator, restored, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PrincipalRecord> PersistAsync(KeyLoadClient administrator,
        PrincipalRecord definition, CancellationToken cancellationToken)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(
            Guid.NewGuid(), definition, cancellationToken).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(definition, actual);
        return actual;
    }
}
