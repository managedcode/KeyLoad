namespace KeyLoad.Server;

/// <summary>Static operator guidance returned identically by the native resource and prompt.</summary>
internal static class McpAgentGuideMarkdown
{
    internal const string Text = """
        # KeyLoad agent quickstart

        KeyLoad is a unified database for AI agents and is currently a development preview. It brings documents, tables, graphs, vectors and search, files, queues, events, and time series together while the product and its compatibility gates continue to develop. Do not assume full SQL-client or protocol conformance, production readiness, or an operation that has not been listed and qualified.

        ## Discover and invoke an operation

        Search with `gateway_tools_search`, or route a short task description with `gateway_tools_route`. Inspect the exact returned tool name, complete input schema, output schema, and read-only/idempotent/destructive hints before invoking it. Invoke only a current canonical operation with arguments matching that schema. For example, call `gateway_tools_search` with:

        ```json
        {"query":"keyload_query_capabilities","maxResults":1}
        ```

        Then inspect the selected tool and invoke its no-body capability query through `gateway_tool_invoke` with:

        ```json
        {"toolId":"keyload_query_capabilities","arguments":{}}
        ```

        Tool discovery documents operations; it grants no access. Persisted server-side rights are checked on each request. Never send credentials, secrets, or caller-supplied roles in tool arguments. Use the operation's returned cursor to continue a bounded read, and stop when no cursor is returned.

        ## Writes and uncertain outcomes

        Use the exact operation schema and retain the same authenticated identity for a retry. If a write returns an unknown outcome or the connection ends after submission, retry only with the same command identity and byte-equivalent payload. Do not create a new command identity to retry an uncertain write; that could apply it twice. Cancellation or a lost response does not prove that a write rolled back.

        This guide is static documentation, not a data browser or authorization grant. Confirm current tool schemas and feature support for each task.
        """;
}
