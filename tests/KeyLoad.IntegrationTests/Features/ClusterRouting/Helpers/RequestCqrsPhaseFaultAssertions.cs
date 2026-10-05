using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Checks public state and probe ownership without disclosing private test values in failure output.</summary>
internal static class RequestCqrsPhaseFaultAssertions
{
    internal static async Task VerifyDocumentAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsPhaseFaultIdentity identity, string expectedJson, long expectedRevision,
        CancellationToken cancellationToken)
    {
        var reference = new EntityRef(identity.Partition, RequestCqrsRf3Protocol.AdminCollection,
            RequestCqrsRf3Protocol.DocumentId);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.GetAsync(reference,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<DocumentResult?>(await administrator.Mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), cancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);
        await Assert.That(sdk?.Reference).IsEqualTo(reference);
        await Assert.That(mcp.Value?.Reference).IsEqualTo(reference);
        await Assert.That(sdk?.Revision).IsEqualTo(expectedRevision);
        await Assert.That(mcp.Value?.Revision).IsEqualTo(expectedRevision);
        await Assert.That(sdk?.Json).IsEqualTo(expectedJson);
        await Assert.That(mcp.Value?.Json).IsEqualTo(expectedJson);
        await Assert.That(sdk?.Redacted).IsFalse();
        await Assert.That(mcp.Value?.Redacted).IsFalse();
    }

    internal static async Task VerifyMarkerAsync(RequestCqrsProbeMarkerRecord marker, Guid armId,
        Guid commandId, RequestCqrsProbePhase phase, IReadOnlyList<ReplicaSiloDiscovery> discovery)
    {
        await Assert.That(marker.ArmId).IsEqualTo(armId);
        await Assert.That(marker.CommandId).IsEqualTo(commandId);
        await Assert.That(marker.RequestId).IsNotEqualTo(Guid.Empty);
        await Assert.That(marker.Phase).IsEqualTo(phase);
        await Assert.That(marker.Outcome).IsEqualTo(RequestCqrsProbeOutcome.Observed);
        var voter = discovery.SingleOrDefault(item => item.VoterId == marker.Voter);
        await Assert.That(voter).IsNotNull();
        await Assert.That(voter!.SiloAddress).IsEqualTo(marker.SiloAddress);
    }

    internal static async Task VerifySettledAsync(RequestCqrsProbeFixture controls, Guid armId,
        Guid requestId, Guid commandId, IReadOnlyList<ReplicaSiloDiscovery> discovery,
        CancellationToken cancellationToken)
    {
        await controls.WaitForSettlementAsync(armId, discovery, cancellationToken).ConfigureAwait(false);
        await Assert.That(controls.ArmFor(armId).Settled).IsTrue();
        var disposed = await controls.WaitForMarkerAsync(armId, RequestCqrsProbePhase.ProducerDisposed,
            RequestCqrsProbeOutcome.Observed, discovery, cancellationToken).ConfigureAwait(false);
        await Assert.That(disposed.RequestId).IsEqualTo(requestId);
        await Assert.That(disposed.CommandId).IsEqualTo(commandId);
        await Assert.That(controls.ArmFor(armId).ProducerDisposedSeen).IsTrue();
    }
}
