using System.Net;
using Aspire.Hosting;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardCatalogInterface34Assertions
{
    private const int PriorInterfaceVersion = 3;
    private const int PriorPeerEnvelopeVersion = 3;
    private const string FirstDocumentId = "document-00";
    private const int ExistingRevision = 1;

    internal static async Task AssertReadyOnAllNodesAsync(DistributedApplication app,
        CancellationToken cancellationToken)
    {
        foreach (var node in Nodes())
        {
            using var http = McpCallerHttp.Create(app, node);
            using var response = await http.GetAsync(RequestCqrsRf3Protocol.ReadyUri, cancellationToken)
                .ConfigureAwait(false);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
    }

    internal static async Task AssertPriorDiscoveryAsync(DistributedApplication app,
        NodeEpochRf3Profile profile, CancellationToken cancellationToken)
    {
        foreach (var node in new[] { RequestCqrsRf3Protocol.Node2, RequestCqrsRf3Protocol.Node3 })
        {
            var discovery = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(app, node, profile,
                cancellationToken).ConfigureAwait(false);
            await Assert.That(discovery.ApplicationRpcVersion).IsEqualTo(PriorInterfaceVersion);
            await Assert.That(discovery.PeerEnvelopeVersion).IsEqualTo(PriorPeerEnvelopeVersion);
        }
    }

    internal static async Task AssertInterface4VoterClosedAsync(DistributedApplication app,
        NodeEpochRf3Profile profile, RequestCqrsRf3Workload workload, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(app, RequestCqrsRf3Protocol.Node1);
        using var response = await http.GetAsync(RequestCqrsRf3Protocol.ReadyUri, cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        var sdk = new KeyLoadClient(http, profile.AdminKey, IntegrationClientOptions.Execution());
        var request = new CommandRequest(Guid.NewGuid(), workload.Partition,
            [new PutDocument(RequestCqrsRf3Protocol.AdminCollection, FirstDocumentId,
                RequestCqrsRf3Protocol.ChangedDocumentJson, ExpectedRevision: ExistingRevision,
                ExplicitReplacement: true)]);
        var result = await sdk.CommitAsync(request, cancellationToken).ConfigureAwait(false);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.OwnershipLost));
    }

    private static string[] Nodes()
        => [RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2, RequestCqrsRf3Protocol.Node3];
}
