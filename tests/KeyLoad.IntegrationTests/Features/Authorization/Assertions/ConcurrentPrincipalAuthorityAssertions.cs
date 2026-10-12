using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class ConcurrentPrincipalAuthorityAssertions
{
    internal static Task<global::System.Text.Json.JsonElement> CaptureAsync(ClusterFixture fixture,
        McpDocumentScenario first, McpDocumentScenario second, CancellationToken token)
        => AdversarialAuthorityTrial.WithRootAsync(fixture, McpCallerProtocol.Node1, async root =>
        {
            var firstDocument = await PublicReadAssertions.DocumentAsync(await root.GetAsync(first.Reference, token));
            var secondDocument = await PublicReadAssertions.DocumentAsync(await root.GetAsync(second.Reference, token));
            await SqlRf3Protocol.EqualAsync<DocumentResult?>(Expected(first), firstDocument);
            await SqlRf3Protocol.EqualAsync<DocumentResult?>(Expected(second), secondDocument);
            var firstOutbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(first.Partition, token));
            var secondOutbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(second.Partition, token));
            return global::System.Text.Json.JsonSerializer.SerializeToElement(new
            { FirstDocument = firstDocument, SecondDocument = secondDocument, FirstOutbox = firstOutbox, SecondOutbox = secondOutbox },
                global::KeyLoad.JsonDefaults.Options);
        });

    internal static Task LiteralAsync(McpDocumentScenario scenario, DocumentResult actual)
        => SqlRf3Protocol.EqualAsync(Expected(scenario), actual);

    internal static async Task FollowingAsync(McpOfficialClient firstClient, McpOfficialClient secondClient,
        McpDocumentScenario first, McpDocumentScenario second, CancellationToken token)
    {
        var firstRead = await PublicReadAssertions.RequiredDocumentAsync(await firstClient.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(first.Reference), token));
        var secondRead = await PublicReadAssertions.RequiredDocumentAsync(await secondClient.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(second.Reference), token));
        await LiteralAsync(first, firstRead.Value);
        await LiteralAsync(second, secondRead.Value);
    }

    private static DocumentResult Expected(McpDocumentScenario scenario)
        => new(scenario.Reference, McpCallerProtocol.FirstRevision, McpDocumentProtocol.InitialJson, false, []);
}
