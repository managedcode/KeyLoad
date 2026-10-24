using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class InitialCredentialAuthorityFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, string? invalidKey, CancellationToken token)
    {
        var state = await InitialCredentialAuthoritySetup.CreateAsync(fixture, token);
        await InitialCredentialAuthorityClients.RunAsync(fixture, state, invalidKey, token);
        var image = await AdversarialAuthorityTrial.WithRootAsync(fixture, McpCallerProtocol.Node1,
            root => InitialCredentialAuthorityValues.ImageAsync(root, state, token));
        await CredentialAuthorityCold.RestartAsync(fixture, token);
        await SqlClusterProtocol.EqualAsync(image, await AdversarialAuthorityTrial.WithRootAsync(fixture, McpCallerProtocol.Node1,
            root => InitialCredentialAuthorityValues.ImageAsync(root, state, token)));
        await InitialCredentialAuthorityClients.RunAsync(fixture, state, invalidKey, token);
    }
}
