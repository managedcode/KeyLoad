using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class InitialCredentialAuthorityClients
{
    internal static async Task RunAsync(ClusterFixture fixture, InitialCredentialAuthorityState state,
        string? invalidKey, CancellationToken token)
    {
        var failures = new List<Exception>();
        try
        {
            await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
                state.Identity.Secret, token);
            try
            {
                await session.Client.ListToolsAsync(cancellationToken: token);
                await WithHttpAsync(fixture, session, state, invalidKey, token);
            }
            catch (Exception original) when (NativeCqrsBoundaryErrors.IsNonFatal(original)) { failures.Add(original); }
            catch (Exception original) when (!NativeCqrsBoundaryErrors.IsNonFatal(original)) { failures.Add(original); }
        }
        catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WithHttpAsync(ClusterFixture fixture, McpOfficialClient session,
        InitialCredentialAuthorityState state, string? invalidKey, CancellationToken token)
    {
        var failures = new List<Exception>();
        try
        {
            using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
            try
            {
                var root = new global::KeyLoad.Client.KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
                var reader = new global::KeyLoad.Client.KeyLoadClient(http, state.Identity.Secret, IntegrationClientOptions.Execution());
                await InitialCredentialAuthorityValues.HealthyAsync(root, reader, session, state, token);
                await InitialCredentialAuthorityRefusals.VerifyAsync(fixture, root, state, invalidKey, token);
                await InitialCredentialAuthorityValues.ReplayAsync(root, state, token);
                await InitialCredentialAuthorityValues.HealthyAsync(root, reader, session, state, token);
            }
            catch (Exception original) when (NativeCqrsBoundaryErrors.IsNonFatal(original)) { failures.Add(original); }
            catch (Exception original) when (!NativeCqrsBoundaryErrors.IsNonFatal(original)) { failures.Add(original); }
        }
        catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
