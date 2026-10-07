using System.Net;
using Aspire.Hosting;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3SurvivorOracle
{
    internal static async Task VerifyAsync(DistributedApplication app, NodeEpochRf3Profile profile,
        RequestCqrsRf3Workload workload, IReadOnlyList<NodeEpochRf3NodeObservation> original,
        string stoppedNode, CancellationToken cancellationToken)
    {
        foreach (var name in new[] { RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2,
                     RequestCqrsRf3Protocol.Node3 }.Where(name => name != stoppedNode))
        {
            var prior = original.Single(node => node.Name == name);
            await VerifySurvivorAsync(app, name, stoppedNode, profile, workload, prior, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private const string PhaseDataKey = "KeyLoad.C1.Phase";
    private const string NodeDataKey = "KeyLoad.C1.IngressNode";
    private const string StoppedNodeDataKey = "KeyLoad.C1.StoppedNode";
    private const string FollowerFailurePhase = "AfterFollowerKillBeforeRestart";
    private const string FailurePhaseLabel = "RF3 phase=";
    private const string IngressNodeLabel = "; ingressNode=";
    private const string StoppedNodeLabel = "; stoppedNode=";
    private const string ObservedNodeIdLabel = "; observedNodeId=";
    private const string SdkErrorCodeLabel = "; sdkErrorCode=";
    private const string SdkSafeDetailLabel = "; sdkSafeDetail=";
    private const string MissingProblemValue = "missing";

    private static async Task VerifySurvivorAsync(DistributedApplication app, string node,
        string stoppedNode, NodeEpochRf3Profile profile, RequestCqrsRf3Workload workload,
        NodeEpochRf3NodeObservation original, CancellationToken cancellationToken)
    {
        using var health = McpCallerHttp.Create(app, node);
        using var response = await health.GetAsync(RequestCqrsRf3Protocol.ReadyUri, cancellationToken).ConfigureAwait(false);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        RequestCqrsRf3Callers connected;
        try
        { connected = await RequestCqrsRf3Callers.ConnectAsync(app, node, profile.AdminKey, cancellationToken).ConfigureAwait(false); }
        catch (Exception primary)
        {
            primary.Data[PhaseDataKey] = FollowerFailurePhase;
            primary.Data[NodeDataKey] = node;
            primary.Data[StoppedNodeDataKey] = stoppedNode;
            throw;
        }
        await using var callers = connected;
        var status = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.StatusAsync(cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(status.RoutingReady).IsTrue();
        await Assert.That(status.Voters).IsEqualTo(RequestCqrsRf3Protocol.NodeCount);
        await Assert.That(status.NodeId).IsEqualTo(original.Status.NodeId);
        await Assert.That(status.Incarnation).IsEqualTo(profile.Incarnation);
        await Assert.That(status.Applied).IsGreaterThanOrEqualTo(workload.SeedReceipt.Token.Position);
        var reference = new EntityRef(workload.Partition, RequestCqrsRf3Protocol.AdminCollection, "document-00");
        var sdkResult = await callers.Sdk.GetAsync(reference, cancellationToken).ConfigureAwait(false);
        var problem = sdkResult.Problem;
        var failureContext = string.Concat(FailurePhaseLabel, FollowerFailurePhase, IngressNodeLabel, node,
            StoppedNodeLabel, stoppedNode, ObservedNodeIdLabel, original.Status.NodeId,
            SdkErrorCodeLabel, problem?.ErrorCode ?? MissingProblemValue,
            SdkSafeDetailLabel, problem?.Detail ?? MissingProblemValue);
        await Assert.That(sdkResult.IsSuccess).IsTrue().Because(failureContext);
        var sdk = sdkResult.Value;
        var mcp = await McpCallerAssertions.SuccessAsync<DocumentResult?>(await callers.Mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), cancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);
        await Assert.That(sdk?.Json).IsEqualTo(workload.InitialDocuments[0]);
        await Assert.That(mcp.Value?.Json).IsEqualTo(workload.InitialDocuments[0]);
        await Assert.That(sdk?.Revision).IsEqualTo(1L);
        await Assert.That(mcp.Value?.Revision).IsEqualTo(1L);
    }
}
