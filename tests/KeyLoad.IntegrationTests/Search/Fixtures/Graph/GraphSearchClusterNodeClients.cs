using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

/// <summary>Owns actual Aspire-discovered administrative and reader HTTP clients for all Orleans nodes.</summary>
internal sealed class GraphSearchClusterNodeClients : IDisposable
{
    private readonly HttpClient[] adminHttp;
    private readonly HttpClient[] readerHttp;

    internal KeyLoadClient[] Administrators
    {
        get;
    }
    internal KeyLoadClient[] Readers
    {
        get;
    }
    internal string ReaderSecret
    {
        get;
    }

    internal GraphSearchClusterNodeClients(ClusterFixture fixture, string readerSecret, string[] nodes)
    {
        ReaderSecret = readerSecret;
        adminHttp = nodes.Select(node => McpCallerHttp.Create(fixture, node)).ToArray();
        readerHttp = nodes.Select(node => McpCallerHttp.Create(fixture, node)).ToArray();
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
