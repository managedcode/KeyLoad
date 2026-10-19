namespace KeyLoad.Server;

/// <summary>Closed execution categories; missing request provenance is Other.</summary>
internal enum McpPipelineMethodCategory
{
    Other,
    Initialize,
    ServerDiscover,
    ToolsCall
}
