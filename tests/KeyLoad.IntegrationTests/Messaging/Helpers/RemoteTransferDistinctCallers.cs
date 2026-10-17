using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferDistinctCallers
{
    internal static async Task WithAsync(TwoRf3MembershipWave wave, RemoteTransferColdSeed seed,
        Func<KeyLoadClient, McpOfficialClient, Task> execute, CancellationToken token)
    {
        var failures = new List<Exception>();
        using var http = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node1);
        var sdk = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            seed.Identity.Secret, token);
        await ServerFailureObserver.ObserveAsync(() => execute(sdk, mcp), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
