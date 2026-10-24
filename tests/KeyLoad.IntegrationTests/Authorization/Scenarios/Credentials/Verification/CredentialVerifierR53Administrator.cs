using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class CredentialVerifierR53Administrator
{
    internal static async Task RunAsync(ClusterFixture fixture,
        Func<McpOfficialClient, Task> action, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var administrator = await McpOfficialClient.ConnectAsync(fixture,
                McpCallerProtocol.Node3, fixture.AdminKey, token);
            await ServerFailureObserver.ObserveAsync(() => action(administrator), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
