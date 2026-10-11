using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SensitiveAuthorityClients
{
    internal static async Task<T> RunAsync<T>(ClusterFixture fixture, string key,
        Func<KeyLoadClient, KeyLoadClient, McpOfficialClient, Task<T>> operation, CancellationToken token)
    {
        var failures = new List<Exception>();
        T value = default!;
        try
        {
            using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node3);
            try
            {
                await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, key, token);
                try
                {
                    value = await operation(new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution()),
                        new KeyLoadClient(http, key, IntegrationClientOptions.Execution()), session);
                }
                catch (Exception original) when (NativeCqrsBoundaryErrors.IsNonFatal(original)) { failures.Add(original); }
                catch (Exception original) when (!NativeCqrsBoundaryErrors.IsNonFatal(original)) { failures.Add(original); }
            }
            catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
            catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        }
        catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
        return value;
    }
}
