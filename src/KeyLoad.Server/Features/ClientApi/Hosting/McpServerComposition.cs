using ManagedCode.MCPGateway;
using ManagedCode.MCPGateway.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KeyLoad.Server;

/// <summary>Registers the official stateless MCP server without a second protocol or database engine.</summary>
internal static class McpServerComposition
{
    private const string ServerName = "KeyLoad";
    private const string ServerVersion = "1.0.0";
    private const string SdkLoggerPrefix = "ModelContextProtocol";
    private const string Instructions = "Read keyload://guides/agent-quickstart or request keyload_agent_quickstart for usage. "
        + "Find relevant operations with gateway_tools_search or gateway_tools_route. "
        + "Use the returned exact schema with gateway_tool_invoke. Retry writes with the same command identity and payload. "
        + "Cancellation does not prove rollback. Paginate bounded database reads using their returned cursors.";

    /// <summary>Registers fresh per-request native handlers and independent retained-memory ownership.</summary>
    internal static void Register(WebApplicationBuilder builder)
    {
        builder.Logging.AddFilter(SdkLoggerPrefix, LogLevel.None);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddMcpGateway();
        builder.Services.AddSingleton(services => new McpGatewayCatalogOwner(
            services.GetRequiredService<IMcpGatewayFactory>(), services.GetRequiredService<IHttpContextAccessor>()));
        builder.Services.AddHostedService(services => new McpGatewayCatalogWarmup(
            services.GetRequiredService<McpGatewayCatalogOwner>()));
        builder.Services.AddSingleton(provider =>
        {
            var memory = provider.GetRequiredService<IOptions<NodeOptions>>().Value.McpMemory;
            return new McpMemoryBudget(memory.DataBytes, memory.ControlBytes, memory.IngressBytes);
        });
        builder.Services.AddMcpServer(options =>
        {
            options.ProtocolVersion = McpTransportProtocol.Revision;
            options.ServerInfo = new Implementation { Name = ServerName, Version = ServerVersion };
            options.ServerInstructions = Instructions;
            options.Capabilities = new ServerCapabilities
            {
                Tools = new ToolsCapability { ListChanged = false },
                Resources = new ResourcesCapability { Subscribe = false, ListChanged = false },
                Prompts = new PromptsCapability { ListChanged = false }
            };
        }).WithHttpTransport(options =>
        {
            options.SessionMode = HttpServerSessionMode.Stateless;
            options.ConfigureSessionOptions = ConfigureAsync;
        });
    }

    private static Task ConfigureAsync(HttpContext context, McpServerOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = context.Items[McpHttpPipeline.StateItem] as McpRequestState
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);
        var dispatcher = new McpToolDispatcher(context, state);
        options.Handlers.ListToolsHandler = dispatcher.ListAsync;
        options.Handlers.CallToolHandler = dispatcher.CallAsync;
        options.Handlers.ListResourcesHandler = McpAgentGuideHandlers.ListResourcesAsync;
        options.Handlers.ReadResourceHandler = McpAgentGuideHandlers.ReadResourceAsync;
        options.Handlers.ListPromptsHandler = McpAgentGuideHandlers.ListPromptsAsync;
        options.Handlers.GetPromptHandler = McpAgentGuideHandlers.GetPromptAsync;
        var pipeline = new McpSessionPipeline(context, state);
        options.Filters.Message.IncomingFilters.Add(pipeline.Incoming);
        options.Filters.Message.OutgoingFilters.Add(pipeline.Outgoing);
        return Task.CompletedTask;
    }
}
