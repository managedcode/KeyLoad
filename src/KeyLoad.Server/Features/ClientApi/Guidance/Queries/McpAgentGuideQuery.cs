using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Validates requests and creates fresh official MCP resource and prompt DTOs.</summary>
internal static class McpAgentGuideQuery
{
    internal static ListResourcesResult ListResources(string? cursor, CancellationToken cancellationToken)
    {
        ValidateCursor(cursor, cancellationToken);
        ValidateContent();
        return new ListResourcesResult
        {
            Resources = [new Resource
            {
                Uri = McpAgentGuideContract.ResourceUri,
                Name = McpAgentGuideContract.ResourceName,
                Description = McpAgentGuideContract.Description,
                MimeType = McpAgentGuideContract.MimeType
            }],
            NextCursor = null
        };
    }

    internal static ReadResourceResult ReadResource(string? uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateContent();
        if (!string.Equals(uri, McpAgentGuideContract.ResourceUri, StringComparison.Ordinal))
        { throw Protocol(McpAgentGuideContract.UnknownResource); }
        return new ReadResourceResult
        {
            Contents = [new TextResourceContents
            {
                Uri = McpAgentGuideContract.ResourceUri,
                MimeType = McpAgentGuideContract.MimeType,
                Text = McpAgentGuideMarkdown.Text
            }]
        };
    }

    internal static ListPromptsResult ListPrompts(string? cursor, CancellationToken cancellationToken)
    {
        ValidateCursor(cursor, cancellationToken);
        ValidateContent();
        return new ListPromptsResult
        {
            Prompts = [new Prompt
            {
                Name = McpAgentGuideContract.PromptName,
                Description = McpAgentGuideContract.Description,
                Arguments = []
            }],
            NextCursor = null
        };
    }

    internal static GetPromptResult GetPrompt(string? name, IDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateContent();
        if (!string.Equals(name, McpAgentGuideContract.PromptName, StringComparison.Ordinal))
        { throw Protocol(McpAgentGuideContract.UnknownPrompt); }
        if (arguments is { Count: > 0 })
        { throw Protocol(McpAgentGuideContract.PromptArguments); }
        return new GetPromptResult
        {
            Description = McpAgentGuideContract.Description,
            Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = McpAgentGuideMarkdown.Text } }]
        };
    }

    private static void ValidateCursor(string? cursor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrEmpty(cursor))
        { throw Protocol(McpAgentGuideContract.InvalidCursor); }
    }

    private static void ValidateContent()
    {
        if (Encoding.UTF8.GetByteCount(McpAgentGuideMarkdown.Text) > McpAgentGuideContract.MaximumUtf8Bytes)
        { throw new InvalidOperationException(McpAgentGuideContract.Description); }
    }

    private static McpProtocolException Protocol(string message) => new(message, McpErrorCode.InvalidParams);
}
