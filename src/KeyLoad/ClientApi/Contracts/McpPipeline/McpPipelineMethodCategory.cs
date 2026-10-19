namespace KeyLoad.Server;

/// <summary>Closed native categories; missing request provenance is Other.</summary>
internal enum McpPipelineMethodCategory
{
    Other,
    Initialize,
    ServerDiscover,
    ToolsCall
}
