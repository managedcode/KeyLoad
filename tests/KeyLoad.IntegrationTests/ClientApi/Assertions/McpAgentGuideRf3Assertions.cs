using System.Text;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>Independent assertions for the official MCP resource and prompt APIs.</summary>
internal static class McpAgentGuideRf3Assertions
{
    internal const string ResourceUri = "keyload://guides/agent-quickstart";
    internal const string ResourceName = "KeyLoad agent quickstart";
    internal const string PromptName = "keyload_agent_quickstart";
    internal const string MimeType = "text/markdown";
    internal const string UnknownUri = "keyload://guides/missing";
    internal const string UnknownPrompt = "keyload_agent_missing";
    internal const int MaximumBytes = 8192;
    private const string UnexpectedArgument = "unexpected";
    private static readonly Uri GuideResource = new(ResourceUri, UriKind.Absolute);
    private static readonly Uri MissingResource = new(UnknownUri, UriKind.Absolute);

    internal static async Task VerifyGuideAsync(McpOfficialClient session, CancellationToken cancellationToken)
    {
        var listedResources = await session.Client.ListResourcesAsync(new ListResourcesRequestParams(), cancellationToken);
        await Assert.That(listedResources.NextCursor).IsNull();
        await Assert.That(listedResources.Resources.Count).IsEqualTo(1);
        await Assert.That(listedResources.Resources[0].Uri).IsEqualTo(ResourceUri);
        await Assert.That(listedResources.Resources[0].Name).IsEqualTo(ResourceName);
        await Assert.That(listedResources.Resources[0].MimeType).IsEqualTo(MimeType);
        listedResources.Resources[0].Name = UnknownPrompt;
        await VerifyFreshResourceListAsync(session, cancellationToken);
        var resourceText = await ReadGuideTextAsync(session, cancellationToken);
        var promptText = await ReadPromptTextAsync(session, cancellationToken);
        await Assert.That(resourceText).IsEqualTo(promptText);
        await Assert.That(Encoding.UTF8.GetByteCount(resourceText) <= MaximumBytes).IsTrue();
        await Assert.That(resourceText.Contains("{\"query\":\"keyload_query_capabilities\",\"maxResults\":1}", StringComparison.Ordinal)).IsTrue();
        await Assert.That(resourceText.Contains("{\"toolId\":\"keyload_query_capabilities\",\"arguments\":{}}", StringComparison.Ordinal)).IsTrue();
    }

    internal static async Task VerifyRejectedInputsAsync(McpOfficialClient session, CancellationToken cancellationToken)
    {
        await ExpectInvalidAsync(() => session.Client.ListResourcesAsync(
            new ListResourcesRequestParams { Cursor = "next" }, cancellationToken).AsTask());
        await ExpectInvalidAsync(() => session.Client.ReadResourceAsync(MissingResource, cancellationToken: cancellationToken).AsTask());
        await ExpectInvalidAsync(() => session.Client.ListPromptsAsync(
            new ListPromptsRequestParams { Cursor = "next" }, cancellationToken).AsTask());
        await ExpectInvalidAsync(() => session.Client.GetPromptAsync(UnknownPrompt, cancellationToken: cancellationToken).AsTask());
        var arguments = new Dictionary<string, object?> { [UnexpectedArgument] = true };
        await ExpectInvalidAsync(() => session.Client.GetPromptAsync(PromptName, arguments,
            cancellationToken: cancellationToken).AsTask());
    }

    private static async Task VerifyFreshResourceListAsync(McpOfficialClient session, CancellationToken cancellationToken)
    {
        var next = await session.Client.ListResourcesAsync(new ListResourcesRequestParams(), cancellationToken);
        await Assert.That(next.Resources.Single().Name).IsEqualTo(ResourceName);
    }

    private static async Task<string> ReadGuideTextAsync(McpOfficialClient session, CancellationToken cancellationToken)
    {
        var result = await session.Client.ReadResourceAsync(GuideResource, cancellationToken: cancellationToken);
        await Assert.That(result.Contents.Count).IsEqualTo(1);
        var text = (TextResourceContents)result.Contents[0];
        await Assert.That(text.Uri).IsEqualTo(ResourceUri);
        await Assert.That(text.MimeType).IsEqualTo(MimeType);
        return text.Text;
    }

    private static async Task<string> ReadPromptTextAsync(McpOfficialClient session, CancellationToken cancellationToken)
    {
        var prompts = await session.Client.ListPromptsAsync(new ListPromptsRequestParams(), cancellationToken);
        await Assert.That(prompts.NextCursor).IsNull();
        await Assert.That(prompts.Prompts.Count).IsEqualTo(1);
        await Assert.That(prompts.Prompts[0].Name).IsEqualTo(PromptName);
        prompts.Prompts[0].Name = UnknownPrompt;
        var next = await session.Client.ListPromptsAsync(new ListPromptsRequestParams(), cancellationToken);
        await Assert.That(next.Prompts.Single().Name).IsEqualTo(PromptName);
        var result = await session.Client.GetPromptAsync(PromptName, cancellationToken: cancellationToken);
        await Assert.That(result.Messages.Count).IsEqualTo(1);
        return ((TextContentBlock)result.Messages[0].Content).Text;
    }

    private static async Task ExpectInvalidAsync(Func<Task> call)
    {
        var error = await Assert.ThrowsAsync<McpProtocolException>(call);
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.ErrorCode).IsEqualTo(McpErrorCode.InvalidParams);
    }
}
