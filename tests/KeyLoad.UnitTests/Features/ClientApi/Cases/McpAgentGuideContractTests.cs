using System.Text;
using KeyLoad.Server;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCPGW-007: native resource and prompt replies have one exact bounded static guide.</summary>
internal sealed class McpAgentGuideContractTests
{
    private const int MaximumBytes = 8192;
    private const string UnknownUri = "keyload://guides/missing";
    private const string UnknownName = "keyload_agent_missing";
    private const string InvalidCursor = "next";
    private const string UnexpectedArgument = "unexpected";

    [Test]
    public async Task ResourceAndPromptReturnFreshNativeObjectsWithIdenticalContent()
    {
        var resourceList = McpAgentGuideQuery.ListResources(null, UnitMcpOptions.Execution(), CancellationToken.None);
        var promptList = McpAgentGuideQuery.ListPrompts(null, UnitMcpOptions.Execution(), CancellationToken.None);
        var resource = resourceList.Resources.Single();
        var prompt = promptList.Prompts.Single();
        var resourceRead = McpAgentGuideQuery.ReadResource(resource.Uri, UnitMcpOptions.Execution(), CancellationToken.None);
        var promptRead = McpAgentGuideQuery.GetPrompt(prompt.Name, null, UnitMcpOptions.Execution(), CancellationToken.None);
        var resourceText = ((TextResourceContents)resourceRead.Contents.Single()).Text;
        var promptText = ((TextContentBlock)promptRead.Messages.Single().Content).Text;
        await Assert.That(resource.Uri).IsEqualTo(McpAgentGuideExpected.Uri);
        await Assert.That(resource.MimeType).IsEqualTo(McpAgentGuideExpected.MimeType);
        await Assert.That(prompt.Name).IsEqualTo(McpAgentGuideExpected.Name);
        await Assert.That(resourceText).IsEqualTo(promptText);
        await Assert.That(Encoding.UTF8.GetByteCount(resourceText) <= MaximumBytes).IsTrue();
        await Assert.That(resourceText.Contains(McpAgentGuideExpected.SearchExample, StringComparison.Ordinal)).IsTrue();
        await Assert.That(resourceText.Contains(McpAgentGuideExpected.InvokeExample, StringComparison.Ordinal)).IsTrue();
        await Assert.That(resourceText.Contains(McpAgentGuideExpected.SearchTool, StringComparison.Ordinal)).IsTrue();
        await Assert.That(resourceText.Contains(McpAgentGuideExpected.RouteTool, StringComparison.Ordinal)).IsTrue();
        await Assert.That(resourceText.Contains(McpAgentGuideExpected.InvokeTool, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ReturnedMutableMetadataIsNotSharedBetweenRequests()
    {
        var resources = McpAgentGuideQuery.ListResources(null, UnitMcpOptions.Execution(), CancellationToken.None);
        var prompts = McpAgentGuideQuery.ListPrompts(null, UnitMcpOptions.Execution(), CancellationToken.None);
        resources.Resources[0].Name = UnknownName;
        prompts.Prompts[0].Name = UnknownName;
        var nextResource = McpAgentGuideQuery.ListResources(null, UnitMcpOptions.Execution(), CancellationToken.None).Resources.Single();
        var nextPrompt = McpAgentGuideQuery.ListPrompts(null, UnitMcpOptions.Execution(), CancellationToken.None).Prompts.Single();
        await Assert.That(nextResource.Name).IsEqualTo("KeyLoad agent quickstart");
        await Assert.That(nextPrompt.Name).IsEqualTo(McpAgentGuideExpected.Name);
    }

    [Test]
    public async Task UnknownInputsCursorsAndPromptArgumentsFailClosed()
    {
        await AssertInvalidAsync(() => McpAgentGuideQuery.ReadResource(UnknownUri, UnitMcpOptions.Execution(), CancellationToken.None));
        await AssertInvalidAsync(() => McpAgentGuideQuery.GetPrompt(UnknownName, null, UnitMcpOptions.Execution(), CancellationToken.None));
        await AssertInvalidAsync(() => McpAgentGuideQuery.ListResources(InvalidCursor, UnitMcpOptions.Execution(), CancellationToken.None));
        await AssertInvalidAsync(() => McpAgentGuideQuery.ListPrompts(InvalidCursor, UnitMcpOptions.Execution(), CancellationToken.None));
        var arguments = new Dictionary<string, System.Text.Json.JsonElement> { [UnexpectedArgument] = default };
        await AssertInvalidAsync(() => McpAgentGuideQuery.GetPrompt(McpAgentGuideExpected.Name, arguments, UnitMcpOptions.Execution(), CancellationToken.None));
    }

    [Test]
    public async Task CancellationIsObservedBeforeReturningAnyReply()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var listError = Assert.ThrowsExactly<OperationCanceledException>(() =>
            McpAgentGuideQuery.ListResources(null, UnitMcpOptions.Execution(), cancellation.Token));
        var readError = Assert.ThrowsExactly<OperationCanceledException>(() =>
            McpAgentGuideQuery.ReadResource(McpAgentGuideExpected.Uri, UnitMcpOptions.Execution(), cancellation.Token));
        var promptListError = Assert.ThrowsExactly<OperationCanceledException>(() =>
            McpAgentGuideQuery.ListPrompts(null, UnitMcpOptions.Execution(), cancellation.Token));
        var promptError = Assert.ThrowsExactly<OperationCanceledException>(() =>
            McpAgentGuideQuery.GetPrompt(McpAgentGuideExpected.Name, null, UnitMcpOptions.Execution(), cancellation.Token));
        await Assert.That(listError).IsNotNull();
        await Assert.That(readError).IsNotNull();
        await Assert.That(promptListError).IsNotNull();
        await Assert.That(promptError).IsNotNull();
    }

    private static async Task AssertInvalidAsync(Action action)
    {
        var error = Assert.ThrowsExactly<McpProtocolException>(action);
        await Assert.That(error.ErrorCode).IsEqualTo(McpErrorCode.InvalidParams);
    }
}
