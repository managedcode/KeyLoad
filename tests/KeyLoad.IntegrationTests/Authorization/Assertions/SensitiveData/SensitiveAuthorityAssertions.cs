using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SensitiveAuthorityAssertions
{
    internal static Task RedactedAsync(McpDocumentScenario scenario, DocumentResult actual)
        => DocumentAsync(new DocumentResult(scenario.StoredReference, McpCallerProtocol.FirstRevision,
            SensitiveAuthorityProtocol.RedactedJson, true, [McpDocumentProtocol.ProtectedPath]), actual);

    internal static Task RawAsync(McpDocumentScenario scenario, DocumentResult actual, bool patched)
        => DocumentAsync(new DocumentResult(scenario.StoredReference,
            patched ? SensitiveAuthorityProtocol.PatchedRevision : McpCallerProtocol.FirstRevision,
            patched ? SensitiveAuthorityProtocol.PatchedJson : McpDocumentProtocol.InitialJson, false, []), actual);

    private static async Task DocumentAsync(DocumentResult expected, DocumentResult actual)
    {
        await SqlClusterProtocol.EqualAsync(expected with
        {
            Json = string.Empty
        }, actual with
        {
            Json = string.Empty
        });
        using var expectedBody = JsonDocument.Parse(expected.Json);
        using var actualBody = JsonDocument.Parse(actual.Json);
        await SqlClusterProtocol.EqualAsync(expectedBody.RootElement, actualBody.RootElement);
    }

    internal static async Task<SensitiveAuthorityImage> ImageAsync(KeyLoadClient root,
        McpDocumentScenario scenario, bool patched, CancellationToken token)
    {
        var document = await PublicReadAssertions.DocumentAsync(await root.GetAsync(scenario.Reference, token));
        await Assert.That(document).IsNotNull();
        await RawAsync(scenario, document!, patched);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(scenario.Partition, token));
        return new(document!, outbox);
    }

    internal static async Task HealthyAsync(KeyLoadClient reader, McpOfficialClient session,
        McpDocumentScenario scenario, bool patched, CancellationToken token)
    {
        var sdk = await PublicReadAssertions.DocumentAsync(await reader.GetAsync(scenario.Reference, token));
        await Assert.That(sdk).IsNotNull();
        await RawAsync(scenario, sdk!, patched);
        var mcp = await PublicReadAssertions.RequiredDocumentAsync(await session.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(scenario.Reference), token));
        await RawAsync(scenario, mcp.Value, patched);
    }
}
