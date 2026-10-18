using System.Net;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardCatalogClusterAssertions
{
    private static readonly string[] Nodes =
    [RequestCqrsClusterProtocol.Node1, RequestCqrsClusterProtocol.Node2, RequestCqrsClusterProtocol.Node3];

    internal static Task VerifyAllVotersAsync(ClusterFixture fixture, EntityRef reference,
        string expectedJson, CancellationToken cancellationToken)
        => VerifyAllVotersAsync(fixture.App, fixture.AdminKey, reference, expectedJson, cancellationToken);

    internal static async Task VerifyAllVotersAsync(Aspire.Hosting.DistributedApplication app, string adminKey,
        EntityRef reference, string expectedJson, CancellationToken cancellationToken)
    {
        foreach (var node in Nodes)
        {
            await VerifyVoterAsync(app, adminKey, node, reference, expectedJson, cancellationToken)
            .ConfigureAwait(false);
        }
    }

    internal static async Task VerifyAbsentAsync(Aspire.Hosting.DistributedApplication app, string adminKey,
        EntityRef reference, CancellationToken cancellationToken)
    {
        foreach (var node in Nodes)
        {
            await VerifyAbsentOnVoterAsync(app, adminKey, node, reference, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task VerifyAbsentOnVoterAsync(Aspire.Hosting.DistributedApplication app,
        string adminKey, string node, EntityRef reference, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(app, node);
        var sdk = new KeyLoadClient(http, adminKey, IntegrationClientOptions.Execution());
        await Assert.That(await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(reference, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false)).IsNull();
        await using var mcp = await McpOfficialClient.ConnectAsync(app, node, adminKey, cancellationToken)
            .ConfigureAwait(false);
        var response = await mcp.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(reference), cancellationToken).ConfigureAwait(false);
        var receipt = await PublicReadAssertions.DocumentAsync(response).ConfigureAwait(false);
        await Assert.That(receipt.Value).IsNull();
    }

    private static async Task VerifyVoterAsync(Aspire.Hosting.DistributedApplication app, string adminKey,
        string node, EntityRef reference, string expectedJson, CancellationToken cancellationToken)
    {
        await VerifyReadyAsync(app, node, cancellationToken).ConfigureAwait(false);
        using var http = McpCallerHttp.Create(app, node);
        var sdk = new KeyLoadClient(http, adminKey, IntegrationClientOptions.Execution());
        var document = await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(reference, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await AssertDocumentAsync(document, expectedJson).ConfigureAwait(false);

        await using var mcp = await McpOfficialClient.ConnectAsync(app, node, adminKey, cancellationToken)
            .ConfigureAwait(false);
        var response = await mcp.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(reference), cancellationToken).ConfigureAwait(false);
        var receipt = await PublicReadAssertions.DocumentAsync(response).ConfigureAwait(false);
        await AssertDocumentAsync(receipt.Value, expectedJson).ConfigureAwait(false);
    }

    private static async Task VerifyReadyAsync(Aspire.Hosting.DistributedApplication app, string node,
        CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(app, node);
        using var response = await http.GetAsync(RequestCqrsClusterProtocol.ReadyUri, cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(json.RootElement.GetProperty(PhysicalShardCatalogClusterProtocol.ReadyStatusField).GetString())
            .IsEqualTo(PhysicalShardCatalogClusterProtocol.ReadyStatusValue);
        await Assert.That(json.RootElement.GetProperty(PhysicalShardCatalogClusterProtocol.ReadyVotersField).GetInt32())
            .IsEqualTo(PhysicalShardCatalogClusterProtocol.VoterCount);
    }

    private static async Task AssertDocumentAsync(DocumentResult? document, string expectedJson)
    {
        await Assert.That(document).IsNotNull();
        await Assert.That(document!.Revision).IsEqualTo(1L);
        await Assert.That(document.Json).IsEqualTo(expectedJson);
    }
}
