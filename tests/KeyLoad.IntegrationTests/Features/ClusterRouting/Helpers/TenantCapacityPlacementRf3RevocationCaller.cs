using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class TenantCapacityPlacementRf3RevocationCaller : IAsyncDisposable
{
    private readonly HttpClient http;
    private readonly string credential;
    private McpOfficialClient? official;
    private TenantCapacityPlacementRf3RevocationCaller(HttpClient http, string credential)
    {
        this.http = http;
        this.credential = credential;
        Sdk = new(http, credential, IntegrationClientOptions.Execution());
    }
    internal KeyLoadClient Sdk { get; }
    internal McpOfficialClient Official => official ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);

    internal static async Task<TenantCapacityPlacementRf3RevocationCaller> OpenAsync(
        TwoRf3MembershipWave wave, string credential, CancellationToken token)
    {
        var caller = new TenantCapacityPlacementRf3RevocationCaller(
            McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node1), credential);
        const int NoFailures = 0;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => caller.ReconnectAsync(wave, token), failures);
        if (failures.Count == NoFailures)
        { return caller; }
        await ServerFailureObserver.ObserveAsync(() => caller.DisposeAsync().AsTask(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
    }

    internal async Task ReconnectAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        if (official is { } original)
        {
            official = null;
            await original.DisposeAsync();
        }
        official = await McpOfficialClient.ConnectAsync(wave.Application,
            TwoRf3MembershipProtocol.Node1, credential, token);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        if (official is { } original)
        {
            official = null;
            await ServerFailureObserver.ObserveAsync(() => original.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.Observe(http.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
