using System.Text;
using KeyLoad.IntegrationTests.QueryExecution;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.ClientApi;

internal static class GuideLifecyclePublicGuidance
{
    internal static readonly Uri Resource = new(GuideLifecyclePublicExpected.ResourceUri, UriKind.Absolute);

    internal static async Task HealthyAsync(McpOfficialClient session, CancellationToken token)
    {
        await ResourcesAsync(session, token);
        await PromptsAsync(session, token);
        await McpGatewayDiscoveryAssertions.VerifyAsync(
            (await session.Client.ListToolsAsync(new ListToolsRequestParams(), token)).Tools);
        await McpDiscoveryAssertions.VerifyAsync(await session.Client.DiscoverKeyLoadToolAsync(
            McpCallerTools.DocumentsGet, token));
    }

    private static async Task ResourcesAsync(McpOfficialClient session, CancellationToken token)
    {
        var expected = new ListResourcesResult
        {
            Resources = [new Resource
            {
                Uri = GuideLifecyclePublicExpected.ResourceUri,
                Name = GuideLifecyclePublicExpected.ResourceName,
                Description = GuideLifecyclePublicExpected.Description,
                MimeType = GuideLifecyclePublicExpected.MimeType
            }],
            NextCursor = null,
            TimeToLive = TimeSpan.Zero,
            CacheScope = CacheScope.Private,
            ResultType = GuideLifecyclePublicExpected.CompleteResult
        };
        var actual = await session.Client.ListResourcesAsync(new ListResourcesRequestParams(), token);
        await SqlClusterProtocol.EqualAsync(expected, actual);
        actual.Resources.Single().Name = "mutated-caller-resource";
        await SqlClusterProtocol.EqualAsync(expected,
            await session.Client.ListResourcesAsync(new ListResourcesRequestParams(), token));
        var expectedRead = new ReadResourceResult
        {
            Contents = [new TextResourceContents
            {
                Uri = GuideLifecyclePublicExpected.ResourceUri,
                MimeType = GuideLifecyclePublicExpected.MimeType,
                Text = GuideLifecyclePublicExpected.Markdown
            }],
            TimeToLive = TimeSpan.Zero,
            CacheScope = CacheScope.Private,
            ResultType = GuideLifecyclePublicExpected.CompleteResult
        };
        var read = await session.Client.ReadResourceAsync(Resource, cancellationToken: token);
        await SqlClusterProtocol.EqualAsync(expectedRead, read);
        var content = (TextResourceContents)read.Contents.Single();
        await Assert.That(Encoding.UTF8.GetByteCount(content.Text)).IsLessThanOrEqualTo(GuideLifecyclePublicExpected.MaximumBytes);
        content.Text = "mutated-caller-resource-content";
        await SqlClusterProtocol.EqualAsync(expectedRead,
            await session.Client.ReadResourceAsync(Resource, cancellationToken: token));
    }

    private static async Task PromptsAsync(McpOfficialClient session, CancellationToken token)
    {
        var expected = new ListPromptsResult
        {
            Prompts = [new Prompt
            {
                Name = GuideLifecyclePublicExpected.PromptName,
                Description = GuideLifecyclePublicExpected.Description,
                Arguments = []
            }],
            NextCursor = null,
            TimeToLive = TimeSpan.Zero,
            CacheScope = CacheScope.Private,
            ResultType = GuideLifecyclePublicExpected.CompleteResult
        };
        var actual = await session.Client.ListPromptsAsync(new ListPromptsRequestParams(), token);
        await SqlClusterProtocol.EqualAsync(expected, actual);
        actual.Prompts.Single().Name = "mutated-caller-prompt";
        await SqlClusterProtocol.EqualAsync(expected,
            await session.Client.ListPromptsAsync(new ListPromptsRequestParams(), token));
        var expectedGet = new GetPromptResult
        {
            Description = GuideLifecyclePublicExpected.Description,
            Messages = [new PromptMessage
            {
                Role = Role.User,
                Content = new TextContentBlock { Text = GuideLifecyclePublicExpected.Markdown }
            }],
            ResultType = GuideLifecyclePublicExpected.CompleteResult
        };
        var get = await session.Client.GetPromptAsync(GuideLifecyclePublicExpected.PromptName, cancellationToken: token);
        await SqlClusterProtocol.EqualAsync(expectedGet, get);
        ((TextContentBlock)get.Messages.Single().Content).Text = "mutated-caller-prompt-content";
        await SqlClusterProtocol.EqualAsync(expectedGet, await session.Client.GetPromptAsync(
            GuideLifecyclePublicExpected.PromptName, cancellationToken: token));
    }
}
