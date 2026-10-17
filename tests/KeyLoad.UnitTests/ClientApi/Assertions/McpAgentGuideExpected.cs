namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Independent exact expectations for the bounded static guidance contract.</summary>
internal static class McpAgentGuideExpected
{
    internal const string Uri = "keyload://guides/agent-quickstart";
    internal const string Name = "keyload_agent_quickstart";
    internal const string MimeType = "text/markdown";
    internal const string SearchTool = "gateway_tools_search";
    internal const string RouteTool = "gateway_tools_route";
    internal const string InvokeTool = "gateway_tool_invoke";
    internal const string SearchExample = "{\"query\":\"keyload_query_capabilities\",\"maxResults\":1}";
    internal const string InvokeExample = "{\"toolId\":\"keyload_query_capabilities\",\"arguments\":{}}";
}
