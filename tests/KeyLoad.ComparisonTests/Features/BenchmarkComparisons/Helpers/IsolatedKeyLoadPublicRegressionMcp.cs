using System.Text.Json;
using Aspire.Hosting;
using ModelContextProtocol.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Owns native official MCP APIs and their actual Aspire HTTP connection without implementing transport semantics.</summary>
internal sealed class IsolatedKeyLoadPublicRegressionMcp : IAsyncDisposable
{
    private const string Endpoint = "/mcp";
    private const string Authorization = "Authorization";
    private const string Bearer = "Bearer ";
    private const string GatewayInvoke = "gateway_tool_invoke";
    private const string ToolId = "toolId";
    private const string Arguments = "arguments";
    private readonly HttpClient http;
    private HttpClientTransport? transport;
    private McpClient? client;

    private IsolatedKeyLoadPublicRegressionMcp(HttpClient http) => this.http = http;

    internal static async Task<IsolatedKeyLoadPublicRegressionMcp> ConnectAsync(DistributedApplication app, int node,
        string key, CancellationToken token)
    {
        var owner = new IsolatedKeyLoadPublicRegressionMcp(IsolatedKeyLoadPublicRegressionProtocol.CreateHttp(app, node));
        try
        {
            owner.transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(owner.http.BaseAddress ?? throw new InvalidOperationException("The actual MCP endpoint is missing."), Endpoint),
                TransportMode = HttpTransportMode.StreamableHttp,
                EnableStandaloneGetStream = false,
                AdditionalHeaders = new Dictionary<string, string>(StringComparer.Ordinal) { [Authorization] = Bearer + key }
            }, owner.http);
            owner.client = await McpClient.CreateAsync(owner.transport, new McpClientOptions(), cancellationToken: token);
            return owner;
        }
        catch (Exception)
        {
            await owner.DisposeAsync();
            throw;
        }
    }

    internal async Task<T> SuccessAsync<T>(string operation, object request, CancellationToken token)
        => await IsolatedKeyLoadPublicRegressionAssertions.McpSuccessAsync<T>(await CallAsync(operation, request, token));

    internal async Task ErrorAsync(string operation, object request, ErrorCode code, CancellationToken token,
        string? credential = null, string? privateValue = null)
        => await IsolatedKeyLoadPublicRegressionAssertions.McpErrorAsync(await CallAsync(operation, request, token),
            code, credential, privateValue);

    private async Task<ModelContextProtocol.Protocol.CallToolResult> CallAsync(string operation, object request, CancellationToken token)
    {
        var native = client ?? throw new InvalidOperationException("The actual MCP client is not connected.");
        return await native.CallToolAsync(GatewayInvoke, new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ToolId] = operation,
            [Arguments] = new Dictionary<string, object?>(StringComparer.Ordinal)
            { [IsolatedKeyLoadPublicRegressionProtocol.Request] = JsonSerializer.SerializeToElement(request, request.GetType(), JsonDefaults.Options) }
        },
            cancellationToken: token);
    }

    /// <summary>Closes every owned native resource, with bounded SDK disposal even on failure.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (client is { } native)
            {
                await CloseAsync(native.DisposeAsync());
            }
        }
        finally
        {
            try
            {
                if (transport is { } nativeTransport)
                {
                    await CloseAsync(nativeTransport.DisposeAsync());
                }
            }
            finally
            {
                http.Dispose();
            }
        }
    }

    private static async Task CloseAsync(ValueTask pending)
    {
        var cleanup = pending.AsTask();
        try
        {
            await cleanup.WaitAsync(NativeExecutionPolicyFixture.Harness().Value.KeyLoadMcpCleanupTimeout, TimeProvider.System);
        }
        catch (Exception)
        {
            _ = cleanup.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }
}
