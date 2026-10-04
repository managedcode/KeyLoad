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
    private const string Instructions = "Use the typed request schema. Retry writes with the same command identity and payload. "
        + "Cancellation does not prove rollback. Paginate bounded reads and discovery using their returned cursors.";

    /// <summary>Registers fresh per-request native handlers and independent retained-memory ownership.</summary>
    internal static void Register(WebApplicationBuilder builder, NodeOptions node)
    {
        builder.Logging.AddFilter(SdkLoggerPrefix, LogLevel.None);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(new McpMemoryBudget(node.McpMemory.DataBytes,
            node.McpMemory.ControlBytes, node.McpMemory.IngressBytes));
        builder.Services.AddMcpServer(options =>
        {
            options.ProtocolVersion = McpTransportProtocol.Revision;
            options.ServerInfo = new Implementation { Name = ServerName, Version = ServerVersion };
            options.ServerInstructions = Instructions;
            options.Capabilities = new ServerCapabilities { Tools = new ToolsCapability { ListChanged = false } };
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
        var pipeline = new McpSessionPipeline(context, state);
        options.Filters.Message.IncomingFilters.Add(pipeline.Incoming);
        options.Filters.Message.OutgoingFilters.Add(pipeline.Outgoing);
        return Task.CompletedTask;
    }
}
