using System.Net;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardCatalogRf3Assertions
{
    private static readonly string[] Nodes =
    [RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2, RequestCqrsRf3Protocol.Node3];

    internal static Task VerifyAllVotersAsync(ClusterFixture fixture, EntityRef reference,
        string expectedJson, CancellationToken cancellationToken)
        => VerifyAllVotersAsync(fixture.App, fixture.AdminKey, reference, expectedJson, cancellationToken);

    internal static async Task VerifyAllVotersAsync(Aspire.Hosting.DistributedApplication app, string adminKey,
        EntityRef reference, string expectedJson, CancellationToken cancellationToken)
    {
        foreach (var node in Nodes)
        { await VerifyVoterAsync(app, adminKey, node, reference, expectedJson, cancellationToken)
            .ConfigureAwait(false); }
    }

    internal static async Task VerifyAbsentAsync(Aspire.Hosting.DistributedApplication app, string adminKey,
        EntityRef reference, CancellationToken cancellationToken)
    {
        foreach (var node in Nodes)
        { await VerifyAbsentOnVoterAsync(app, adminKey, node, reference, cancellationToken).ConfigureAwait(false); }
    }

    private static async Task VerifyAbsentOnVoterAsync(Aspire.Hosting.DistributedApplication app,
        string adminKey, string node, EntityRef reference, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(app, node);
        var sdk = new KeyLoadClient(http, adminKey);
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(reference, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false)).IsNull();
        await using var mcp = await McpOfficialClient.ConnectAsync(app, node, adminKey, cancellationToken)
            .ConfigureAwait(false);
        var response = await mcp.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(reference), cancellationToken).ConfigureAwait(false);
        var receipt = await McpCallerAssertions.SuccessAsync<DocumentResult?>(response).ConfigureAwait(false);
        await Assert.That(receipt.Value).IsNull();
    }

    private static async Task VerifyVoterAsync(Aspire.Hosting.DistributedApplication app, string adminKey,
        string node, EntityRef reference, string expectedJson, CancellationToken cancellationToken)
    {
        await VerifyReadyAsync(app, node, cancellationToken).ConfigureAwait(false);
        using var http = McpCallerHttp.Create(app, node);
        var sdk = new KeyLoadClient(http, adminKey);
        var document = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(reference, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await AssertDocumentAsync(document, expectedJson).ConfigureAwait(false);

        await using var mcp = await McpOfficialClient.ConnectAsync(app, node, adminKey, cancellationToken)
            .ConfigureAwait(false);
        var response = await mcp.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(reference), cancellationToken).ConfigureAwait(false);
        var receipt = await McpCallerAssertions.SuccessAsync<DocumentResult?>(response).ConfigureAwait(false);
        await AssertDocumentAsync(receipt.Value, expectedJson).ConfigureAwait(false);
    }

    private static async Task VerifyReadyAsync(Aspire.Hosting.DistributedApplication app, string node,
        CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(app, node);
        using var response = await http.GetAsync(RequestCqrsRf3Protocol.ReadyUri, cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(json.RootElement.GetProperty(PhysicalShardCatalogRf3Protocol.ReadyStatusField).GetString())
            .IsEqualTo(PhysicalShardCatalogRf3Protocol.ReadyStatusValue);
        await Assert.That(json.RootElement.GetProperty(PhysicalShardCatalogRf3Protocol.ReadyVotersField).GetInt32())
            .IsEqualTo(PhysicalShardCatalogRf3Protocol.VoterCount);
    }

    private static async Task AssertDocumentAsync(DocumentResult? document, string expectedJson)
    {
        await Assert.That(document).IsNotNull();
        await Assert.That(document!.Revision).IsEqualTo(1L);
        await Assert.That(document.Json).IsEqualTo(expectedJson);
    }
}
