using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class CanceledCredentialAuthorityAssertions
{
    internal static async Task<DiscoveryAuthorityImage> CaptureAsync(KeyLoadClient reader,
        McpDocumentScenario scenario, CancellationToken token)
    {
        var document = await PublicReadAssertions.DocumentAsync(await reader.GetAsync(scenario.Reference, token));
        await SqlClusterProtocol.EqualAsync<DocumentResult?>(Expected(scenario), document);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(
            await reader.OutboxStatusAsync(scenario.Partition, token));
        return new(document, outbox);
    }

    internal static async Task HealthyAsync(KeyLoadClient reader, McpOfficialClient session,
        McpDocumentScenario scenario, DiscoveryAuthorityImage before, CancellationToken token)
    {
        var sdk = await PublicReadAssertions.DocumentAsync(await reader.GetAsync(scenario.Reference, token));
        await SqlClusterProtocol.EqualAsync<DocumentResult?>(Expected(scenario), sdk);
        await SqlClusterProtocol.EqualAsync(before, await CaptureAsync(reader, scenario, token));
        var official = await PublicReadAssertions.RequiredDocumentAsync(await session.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(scenario.Reference), token));
        await SqlClusterProtocol.EqualAsync(Expected(scenario), official.Value);
        await SqlClusterProtocol.EqualAsync(before, await CaptureAsync(reader, scenario, token));
    }

    private static DocumentResult Expected(McpDocumentScenario scenario)
        => new(scenario.Reference, McpCallerProtocol.FirstRevision, McpDocumentProtocol.InitialJson, false, []);
}

