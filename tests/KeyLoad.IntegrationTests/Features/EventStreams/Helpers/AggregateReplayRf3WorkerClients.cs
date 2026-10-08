using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal sealed class AggregateReplayRf3WorkerClients : IDisposable
{
    internal KeyLoadClient[] Items { get; }
    internal KeyLoadClient[] AdminItems { get; }
    private readonly HttpClient[] clients;
    private readonly HttpClient[] adminClients;

    internal AggregateReplayRf3WorkerClients(ClusterFixture fixture, string secret, string[] nodes)
    {
        clients = nodes.Select(node => McpCallerHttp.Create(fixture, node)).ToArray();
        Items = clients.Select(client => new KeyLoadClient(client, secret, IntegrationClientOptions.Execution())).ToArray();
        adminClients = nodes.Select(node => McpCallerHttp.Create(fixture, node)).ToArray();
        AdminItems = adminClients.Select(client => new KeyLoadClient(client, fixture.AdminKey, IntegrationClientOptions.Execution())).ToArray();
    }

    public void Dispose()
    {
        foreach (var client in clients)
        {
            client.Dispose();
        }
        foreach (var client in adminClients)
        {
            client.Dispose();
        }
    }
}
