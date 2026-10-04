namespace KeyLoad.Server;

/// <summary>Tags rejected MCP transport checks and logs only closed, non-sensitive diagnostic values.</summary>
internal static class McpTransportDiagnostics
{
    private const int FailureEventId = 4;
    private const string FailureMessage = "MCP transport rejected at {Stage} for {MethodCategory}.";
    private const string StageMetadataKeyValue = "KeyLoad.Server.ClientApi.McpTransportStage";
    private const string DiscoveryMethod = "server/discover";
    private const string InitializeMethod = "initialize";
    private const string ToolsCallMethod = "tools/call";
    private const string ToolsListMethod = "tools/list";
    private static readonly Action<ILogger, McpTransportStage, McpTransportMethodCategory, Exception?> LogRejected =
        LoggerMessage.Define<McpTransportStage, McpTransportMethodCategory>(LogLevel.Warning,
            new EventId(FailureEventId), FailureMessage);

    /// <summary>Names the private exception data entry used for a closed rejection stage.</summary>
    internal const string StageMetadataKey = StageMetadataKeyValue;

    /// <summary>Returns whether the exception carries a defined transport guard stage.</summary>
    /// <param name="error">The original guard rejection.</param>
    /// <returns>True only when the metadata is an owned enum value.</returns>
    internal static bool HasStage(KeyLoadException error) =>
        error.Data[StageMetadataKey] is McpTransportStage stage && Enum.IsDefined(stage);

    /// <summary>Logs only sanitized enum metadata and never passes the original exception to the provider.</summary>
    /// <param name="logger">The optional application logger.</param>
    /// <param name="error">The original rejection; its object and text are never logged.</param>
    /// <param name="headers">The actual request headers; raw values are never logged.</param>
    internal static void Log(ILogger? logger, KeyLoadException error, IHeaderDictionary headers)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(headers);
        if (logger is null)
        {
            return;
        }

        var stage = error.Data[StageMetadataKey] is McpTransportStage marked && Enum.IsDefined(marked)
            ? marked
            : McpTransportStage.Unknown;
        LogRejected(logger, stage, MethodCategory(headers), null);
    }

    private static McpTransportMethodCategory MethodCategory(IHeaderDictionary headers)
    {
        var values = headers[McpTransportProtocol.MethodHeader];
        if (values.Count != 1 || values[0] is not { } raw)
        {
            return McpTransportMethodCategory.Other;
        }

        if (raw.Length > McpTransportProtocol.MaximumMethodCharacters)
        {
            return McpTransportMethodCategory.Other;
        }

        var method = raw.AsSpan().Trim();
        if (method.SequenceEqual(DiscoveryMethod))
        {
            return McpTransportMethodCategory.Discovery;
        }
        if (method.SequenceEqual(InitializeMethod))
        {
            return McpTransportMethodCategory.Initialize;
        }
        if (method.SequenceEqual(ToolsCallMethod))
        {
            return McpTransportMethodCategory.ToolsCall;
        }
        return method.SequenceEqual(ToolsListMethod)
            ? McpTransportMethodCategory.ToolsList
            : McpTransportMethodCategory.Other;
    }
}
