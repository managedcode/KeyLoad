using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Records only closed native failure facts, retaining any actual diagnostic failure.</summary>
internal static class McpPipelineFailureDiagnostic
{
    private const int FailureEventId = 5;
    private const int OriginalFailureCount = 1;
    private const string FailureMessage = "MCP native pipeline rejected at {Stage} with {Code} for {MethodCategory}.";
    private static readonly Action<ILogger, McpPipelineFailureStage, ErrorCode, McpPipelineMethodCategory, Exception?> LogRejected =
        LoggerMessage.Define<McpPipelineFailureStage, ErrorCode, McpPipelineMethodCategory>(LogLevel.Warning,
            new EventId(FailureEventId), FailureMessage);

    /// <summary>Emits no exception text and joins diagnostic faults with the unchanged initiating failure.</summary>
    /// <param name="context">The original scoped HTTP owner and its native logger service.</param>
    /// <param name="message">The actual native message; only request method identity is categorized.</param>
    /// <param name="stage">The fixed owning catch boundary.</param>
    /// <param name="original">The original canonical failure, never passed to logging.</param>
    internal static void Write(HttpContext context, JsonRpcMessage message, McpPipelineFailureStage stage,
        KeyLoadException original)
    {
        List<Exception> failures = [original];
        ServerFailureObserver.Observe(() => LogRejected(
            context.RequestServices.GetRequiredService<ILogger<McpSessionPipeline>>(), stage, original.Code,
            Category(message), null), failures);
        if (failures.Count > OriginalFailureCount)
        { ServerFailureObserver.ThrowIfAny(failures); }
    }

    private static McpPipelineMethodCategory Category(JsonRpcMessage message)
        => (message as JsonRpcRequest)?.Method switch
        {
            RequestMethods.Initialize => McpPipelineMethodCategory.Initialize,
            RequestMethods.ServerDiscover => McpPipelineMethodCategory.ServerDiscover,
            RequestMethods.ToolsCall => McpPipelineMethodCategory.ToolsCall,
            _ => McpPipelineMethodCategory.Other
        };
}
