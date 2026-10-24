using KeyLoad.Client;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.ClientApi;

internal sealed class SdkProblemBudgetPublicCallers : IAsyncDisposable
{
    private readonly HttpClient http;
    private SdkProblemBudgetPublicCallers(HttpClient http, KeyLoadClient minimum,
        KeyLoadClient normal, McpOfficialClient mcp)
    {
        this.http = http;
        Minimum = minimum;
        Normal = normal;
        Mcp = mcp;
    }

    internal KeyLoadClient Minimum
    {
        get;
    }
    internal KeyLoadClient Normal
    {
        get;
    }
    internal McpOfficialClient Mcp
    {
        get;
    }

    private static async Task<SdkProblemBudgetPublicCallers> ConnectAsync(ClusterFixture fixture,
        string secret, CancellationToken token)
    {
        var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        try
        {
            var minimum = new KeyLoadClient(http, secret, IntegrationClientOptions.Execution(
                new KeyLoadClientExecutionOptions { MaximumProblemBodyBytes = 1 }));
            var normal = new KeyLoadClient(http, secret, IntegrationClientOptions.Execution());
            var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, secret, token);
            return new(http, minimum, normal, mcp);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(http.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal static async Task<T> RunOwnedAsync<T>(ClusterFixture fixture, string secret,
        Func<SdkProblemBudgetPublicCallers, Task<T>> operation, CancellationToken token)
    {
        var failures = new List<Exception>();
        T result = default!;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var callers = await ConnectAsync(fixture, secret, token);
            await ServerFailureObserver.ObserveAsync(async () => { result = await operation(callers); }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => Mcp.DisposeAsync().AsTask(), failures);
        ServerFailureObserver.Observe(http.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
