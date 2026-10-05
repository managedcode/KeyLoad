using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed class EventProjectionRf3Clients : IDisposable
{
    private readonly HttpClient[] callerHttp;
    private readonly HttpClient[] readerHttp;
    private readonly HttpClient[] administratorHttp;

    internal KeyLoadClient[] Callers { get; }
    internal KeyLoadClient[] Readers { get; }
    internal KeyLoadClient[] Administrators { get; }

    internal EventProjectionRf3Clients(ClusterFixture fixture, EventProjectionRf3Scenario scenario,
        string[] nodes)
    {
        callerHttp = nodes.Select(node => McpCallerHttp.Create(fixture, node)).ToArray();
        readerHttp = nodes.Select(node => McpCallerHttp.Create(fixture, node)).ToArray();
        administratorHttp = nodes.Select(node => McpCallerHttp.Create(fixture, node)).ToArray();
        Callers = callerHttp.Select(client => new KeyLoadClient(client, scenario.WorkerSecret, IntegrationClientOptions.Execution())).ToArray();
        Readers = readerHttp.Select(client => new KeyLoadClient(client, scenario.ReaderSecret, IntegrationClientOptions.Execution())).ToArray();
        Administrators = administratorHttp.Select(client => new KeyLoadClient(client, fixture.AdminKey, IntegrationClientOptions.Execution())).ToArray();
    }

    public void Dispose()
    {
        DisposeAll(callerHttp);
        DisposeAll(readerHttp);
        DisposeAll(administratorHttp);
    }

    private static void DisposeAll(IEnumerable<HttpClient> clients)
    {
        foreach (var client in clients)
        { client.Dispose(); }
    }
}
