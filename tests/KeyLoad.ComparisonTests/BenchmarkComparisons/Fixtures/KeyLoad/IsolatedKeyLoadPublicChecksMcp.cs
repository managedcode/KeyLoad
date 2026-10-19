using System.Text.Json;
using Aspire.Hosting;
using ModelContextProtocol.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Owns execution official MCP APIs and their actual Aspire HTTP connection without implementing transport semantics.</summary>
internal sealed class IsolatedKeyLoadPublicChecksMcp : IAsyncDisposable
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

    private IsolatedKeyLoadPublicChecksMcp(HttpClient http) => this.http = http;

    internal static async Task<IsolatedKeyLoadPublicChecksMcp> ConnectAsync(DistributedApplication app, int node,
        string key, CancellationToken token)
    {
        var owner = new IsolatedKeyLoadPublicChecksMcp(IsolatedKeyLoadPublicChecksProtocol.CreateHttp(app, node));
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
        => await IsolatedKeyLoadPublicChecksAssertions.McpSuccessAsync<T>(await CallAsync(operation, request, token));

    internal async Task<DocumentResult?> DocumentAsync(string operation, object request, CancellationToken token)
    {
        var response = await SuccessAsync<DocumentReadResult>(operation, request, token);
        await Assert.That(response).IsNotNull();
        return response.Document;
    }

    internal async Task<DocumentResult> RequiredDocumentAsync(string operation, object request, CancellationToken token)
    {
        var document = await DocumentAsync(operation, request, token);
        await Assert.That(document).IsNotNull();
        return document!;
    }

    internal async Task<MessageInspection> RequiredMessageAsync(string operation, object request, CancellationToken token)
    {
        var response = await SuccessAsync<MessageInspectionResult>(operation, request, token);
        await Assert.That(response).IsNotNull();
        await Assert.That(response.Message).IsNotNull();
        return response.Message!;
    }

    internal async Task ErrorAsync(string operation, object request, ErrorCode code, CancellationToken token,
        string? credential = null, string? privateValue = null)
        => await IsolatedKeyLoadPublicChecksAssertions.McpErrorAsync(await CallAsync(operation, request, token),
            code, credential, privateValue);

    private async Task<ModelContextProtocol.Protocol.CallToolResult> CallAsync(string operation, object request, CancellationToken token)
    {
        var execution = client ?? throw new InvalidOperationException("The actual MCP client is not connected.");
        return await execution.CallToolAsync(GatewayInvoke, new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ToolId] = operation,
            [Arguments] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [IsolatedKeyLoadPublicChecksProtocol.Request] = JsonSerializer.SerializeToElement(request, request.GetType(), JsonDefaults.Options)
            }
        },
            cancellationToken: token);
    }

    /// <summary>Closes every owned execution resource, with bounded SDK disposal even on failure.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (client is { } execution)
            {
                await CloseAsync(execution.DisposeAsync());
            }
        }
        finally
        {
            try
            {
                if (transport is { } runtimeTransport)
                {
                    await CloseAsync(runtimeTransport.DisposeAsync());
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
            await cleanup.WaitAsync(ExecutionPolicyFixture.Harness().Value.KeyLoadMcpCleanupTimeout, TimeProvider.System);
        }
        catch (Exception)
        {
            _ = cleanup.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }
}
