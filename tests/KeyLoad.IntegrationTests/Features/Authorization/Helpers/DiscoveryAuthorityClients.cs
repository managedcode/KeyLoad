using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class DiscoveryAuthorityClients
{
    internal static async Task RunAsync(ClusterFixture fixture, DiscoveryAuthorityState state, bool changePolicy, CancellationToken token)
    {
        var failures = new List<Exception>();
        try
        {
            await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, state.Identity.Secret, token);
            try
            {
                var firstRequest = await DiscoveryAuthorityValues.FirstAsync(session, state, token);
                await WithHttpAsync(fixture, session, state, firstRequest, changePolicy, token);
            }
            catch (Exception original) when (NativeCqrsBoundaryErrors.IsNonFatal(original)) { failures.Add(original); }
            catch (Exception original) when (!NativeCqrsBoundaryErrors.IsNonFatal(original)) { failures.Add(original); }
        }
        catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WithHttpAsync(ClusterFixture fixture, McpOfficialClient session, DiscoveryAuthorityState state,
        Guid firstRequest, bool changePolicy, CancellationToken token)
    {
        var failures = new List<Exception>();
        try
        {
            using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
            try
            {
                var root = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
                var reader = new KeyLoadClient(http, state.Identity.Secret, IntegrationClientOptions.Execution());
                if (changePolicy)
                { await DiscoveryAuthorityFlow.WarmAsync(fixture, root, reader, session, state, firstRequest, token); }
                else
                {
                    await DiscoveryAuthorityValues.HealthyAsync(root, reader, session, state, token);
                    await DiscoveryAuthorityValues.ReplayAsync(root, state, token);
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
