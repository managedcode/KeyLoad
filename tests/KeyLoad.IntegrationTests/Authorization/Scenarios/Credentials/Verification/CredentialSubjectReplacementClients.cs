using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class CredentialSubjectReplacementClients
{
    internal static async Task RunAsync(ClusterFixture fixture, CredentialSubjectReplacementState state,
        bool warm, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
                state.CurrentSecret, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await session.Client.ListToolsAsync(cancellationToken: token);
                await HttpAsync(fixture, session, state, warm, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task HttpAsync(ClusterFixture fixture, McpOfficialClient session,
        CredentialSubjectReplacementState state, bool warm, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var root = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
                var reader = new KeyLoadClient(http, state.CurrentSecret, IntegrationClientOptions.Execution());
                StandardAspNetAuthorityTrial.AddHeaders(http);
                session.AddUntrustedIdentityHeaders();
                if (warm)
                {
                    await CredentialSubjectReplacementFlow.WarmAsync(root, reader, session, state, token);
                    await CredentialVerifierR53Flow.WarmAsync(fixture, root, reader, session, state, token);
                }
                else
                {
                    await CredentialSubjectReplacementFlow.ReplayedAsync(root, reader, session, state, token);
                    if (state.Verifier is { Active: true })
                    {
                        await CredentialVerifierR53Flow.ContextAsync(fixture, root, reader, session, state, token);
                    }
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
