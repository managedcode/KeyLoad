using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class CredentialAuthorityClients
{
    internal static async Task RunAsync(ClusterFixture fixture, CredentialAuthorityState state, bool expire,
        bool changeCredential, CancellationToken token)
    {
        var failures = new List<Exception>();
        try
        {
            await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
                state.Document.Identity.Secret, token);
            try
            {
                _ = await DiscoveryAuthorityValues.FirstAsync(session, state.Document, token);
                await WithHttpAsync(fixture, session, state, expire, changeCredential, token);
            }
            catch (Exception original) when (NativeCqrsBoundaryErrors.IsNonFatal(original)) { failures.Add(original); }
            catch (Exception original) when (!NativeCqrsBoundaryErrors.IsNonFatal(original)) { failures.Add(original); }
        }
        catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WithHttpAsync(ClusterFixture fixture, McpOfficialClient session, CredentialAuthorityState state,
        bool expire, bool changeCredential, CancellationToken token)
    {
        var failures = new List<Exception>();
        try
        {
            using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
            try
            {
                var root = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
                var reader = new KeyLoadClient(http, state.Document.Identity.Secret, IntegrationClientOptions.Execution());
                if (changeCredential)
                { await CredentialAuthorityFlow.WarmAsync(fixture, root, reader, session, state, expire, token); }
                else
                {
                    await DiscoveryAuthorityValues.HealthyAsync(root, reader, session, state.Document, token);
                    await CredentialAuthorityFlow.VerifyStatusRoutesAsync(fixture, root, state, token);
                    await CredentialAuthorityFlow.ReplayAsync(root, state, token);
                }
            }
            catch (Exception original) when (NativeCqrsBoundaryErrors.IsNonFatal(original)) { failures.Add(original); }
            catch (Exception original) when (!NativeCqrsBoundaryErrors.IsNonFatal(original)) { failures.Add(original); }
        }
        catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
