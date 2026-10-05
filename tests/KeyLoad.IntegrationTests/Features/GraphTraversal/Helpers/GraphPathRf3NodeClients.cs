using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal sealed class GraphPathRf3NodeClients : IDisposable
{
    internal static readonly string[] Nodes =
        [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
    private readonly HttpClient[] adminHttp;
    private readonly HttpClient[] readerHttp;

    internal KeyLoadClient[] Administrators { get; }
    internal KeyLoadClient[] Readers { get; }

    internal GraphPathRf3NodeClients(ClusterFixture fixture, string readerSecret)
    {
        adminHttp = Nodes.Select(node => McpCallerHttp.Create(fixture, node)).ToArray();
        readerHttp = Nodes.Select(node => McpCallerHttp.Create(fixture, node)).ToArray();
        Administrators = adminHttp.Select(http => new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution())).ToArray();
        Readers = readerHttp.Select(http => new KeyLoadClient(http, readerSecret, IntegrationClientOptions.Execution())).ToArray();
    }

    public void Dispose()
    {
        foreach (var client in readerHttp)
        {
            client.Dispose();
        }
        foreach (var client in adminHttp)
        {
            client.Dispose();
        }
    }
}
