using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed class DistributedSearchRf3Callers(TwoRf3MembershipWave wave) : IAsyncDisposable
{
    private readonly List<HttpClient> connections = [];
    private readonly List<McpOfficialClient> sessions = [];

    internal KeyLoadClient Sdk(string node, string secret)
    {
        var http = McpCallerHttp.Create(wave.Application, node);
        connections.Add(http);
        return new(http, secret, IntegrationClientOptions.Execution());
    }

    internal async Task<McpOfficialClient> OfficialAsync(string node, string secret, CancellationToken token)
    {
        var owner = await McpOfficialClient.ConnectAsync(wave.Application, node, secret, token).ConfigureAwait(false);
        sessions.Add(owner);
        return owner;
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        foreach (var session in sessions)
        { await ServerFailureObserver.ObserveAsync(() => session.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        foreach (var http in connections)
        { ServerFailureObserver.Observe(http.Dispose, failures); }
        sessions.Clear();
        connections.Clear();
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
