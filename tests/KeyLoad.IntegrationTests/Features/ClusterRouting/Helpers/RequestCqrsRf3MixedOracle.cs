using System.Net;
using Aspire.Hosting;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3MixedOracle
{
    internal static async Task VerifyAsync(DistributedApplication app, NodeEpochRf3Profile profile,
        RequestCqrsRf3Workload workload, CancellationToken cancellationToken)
    {
        _ = await RequestCqrsRf3DiscoveryOracle.VerifyMixedImagesAsync(app, profile, cancellationToken)
            .ConfigureAwait(false);
        foreach (var node in new[] { RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2 })
        { await VerifyCurrentEndpointAsync(app, node, profile, workload, cancellationToken).ConfigureAwait(false); }
        await VerifyOfficialMcpAsync(app, RequestCqrsRf3Protocol.Node1, profile, workload, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task VerifyCurrentEndpointAsync(DistributedApplication app, string node,
        NodeEpochRf3Profile profile, RequestCqrsRf3Workload workload, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(app, node);
        using var response = await http.GetAsync(RequestCqrsRf3Protocol.ReadyUri, cancellationToken).ConfigureAwait(false);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        var sdk = new KeyLoadClient(http, profile.AdminKey);
        var reference = new EntityRef(workload.Partition, RequestCqrsRf3Protocol.AdminCollection, "document-00");
        var result = await sdk.GetAsync(reference, cancellationToken).ConfigureAwait(false);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.OwnershipLost));
    }

    private static async Task VerifyOfficialMcpAsync(DistributedApplication app, string node,
        NodeEpochRf3Profile profile, RequestCqrsRf3Workload workload, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        var initializing = true;
        try
        {
            await using var callers = await McpOfficialClient.ConnectAsync(app, node, profile.AdminKey,
                cancellationToken).ConfigureAwait(false);
            initializing = false;
            await ServerFailureObserver.ObserveAsync(
                () => VerifyMcpReadAsync(callers, workload, cancellationToken), failures).ConfigureAwait(false);
        }
        catch (HttpRequestException failure) when (initializing && failure.StatusCode == HttpStatusCode.ServiceUnavailable)
        { return; }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task VerifyMcpReadAsync(McpOfficialClient callers, RequestCqrsRf3Workload workload,
        CancellationToken cancellationToken)
    {
        var reference = new EntityRef(workload.Partition, RequestCqrsRf3Protocol.AdminCollection, "document-00");
        var reply = await callers.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), cancellationToken)
            .ConfigureAwait(false);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.OwnershipLost, dispatched: false).ConfigureAwait(false);
    }
}
