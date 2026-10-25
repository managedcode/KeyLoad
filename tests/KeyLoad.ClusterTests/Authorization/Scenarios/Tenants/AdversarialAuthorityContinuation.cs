using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Authorization;

internal static class AdversarialAuthorityContinuation
{
    internal static async Task RunAsync(ClusterFixture fixture, KeyLoadClient root, KeyLoadClient reader,
        McpOfficialClient session, AdversarialAuthorityState state, CancellationToken token)
    {
        await AdversarialAuthorityRefusals.ForeignAsync(root, reader, session, state, token);
        await AdversarialAuthorityAssertions.HealthyAsync(reader, session, state, token);
        await PersistAsync(root, state.Identity.Principal with
        {
            Grants = [],
            PolicyEpoch = AdversarialAuthorityProtocol.RestrictedEpoch
        }, token);
        await AdversarialAuthorityRefusals.OwnedAsync(fixture, root, reader, session, state, ErrorCode.PermissionDenied, token);
        await PersistAsync(root, state.Identity.Principal with
        {
            Revoked = true,
            PolicyEpoch = AdversarialAuthorityProtocol.RevokedEpoch
        }, token);
        await AdversarialAuthorityRefusals.OwnedAsync(fixture, root, reader, session, state, ErrorCode.Unauthenticated, token);
        await PersistAsync(root, state.Identity.Principal with
        {
            PolicyEpoch = AdversarialAuthorityProtocol.RepairedEpoch
        }, token);
        await AdversarialAuthorityAssertions.HealthyAsync(reader, session, state, token);
        await AdversarialAuthorityRefusals.ForeignAsync(root, reader, session, state, token);
    }

    private static async Task PersistAsync(KeyLoadClient root, PrincipalRecord expected, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await SqlClusterProtocol.EqualAsync(expected, actual);
    }
}
