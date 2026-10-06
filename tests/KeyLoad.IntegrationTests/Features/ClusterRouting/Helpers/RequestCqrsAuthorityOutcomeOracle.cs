using Aspire.Hosting;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.ClusterRouting.Processes;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Checks real stored outcomes only after the RF3 wave has relinquished its native owners.</summary>
internal sealed class RequestCqrsAuthorityOutcomeOracle(string dataRoot, NodeEpochRf3Profile profile,
    string principalId, Guid positiveCommandId)
{
    private const string PositiveDocumentId = "c1-outcome-positive-control";
    private const int ExpectedCurrentDataEpoch = 7;
    private const string PositiveJson = "{\"control\":\"stored-outcome\"}";
    private const string MissingCut = "The C1 outcome inspection has no complete captured RF3 cut.";
    private NodeEpochRf3NodeObservation[]? nodes;

    internal bool ReadyToInspect => nodes is { Length: RequestCqrsRf3Protocol.NodeCount };

    internal static async Task<RequestCqrsAuthorityOutcomeOracle> SeedAsync(string dataRoot,
        NodeEpochRf3Profile profile, RequestCqrsAuthorityFaultIdentity identity, RequestCqrsRf3Callers caller,
        CancellationToken cancellationToken)
    {
        var commandId = Guid.NewGuid();
        var command = new CommandRequest(commandId, identity.Partition,
            [new PutDocument(RequestCqrsRf3Protocol.AdminCollection, PositiveDocumentId, PositiveJson, 0)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(receipt.Mutations[0].Id).IsEqualTo(PositiveDocumentId);
        await Assert.That(receipt.Mutations[0].Revision).IsEqualTo(1L);
        return new(dataRoot, profile, identity.Principal.Id, commandId);
    }

    internal async Task CaptureAsync(DistributedApplication app, CancellationToken cancellationToken)
    {
        var captured = await NodeEpochRf3StatusOracle.CaptureAsync(app, profile, cancellationToken)
            .ConfigureAwait(false);
        nodes = captured;
    }

    internal async Task InspectAfterWaveJoinedAsync(Guid heldCommandId, CancellationToken cancellationToken)
    {
        var captured = nodes ?? throw new InvalidOperationException(MissingCut);
        await Assert.That(captured.Length).IsEqualTo(RequestCqrsRf3Protocol.NodeCount);
        var positiveCount = 0;
        foreach (var node in captured)
        {
            var held = await InspectNodeAsync(node, heldCommandId, cancellationToken).ConfigureAwait(false);
            await Assert.That(held.OutcomePresent).IsFalse();
            var positive = await InspectNodeAsync(node, positiveCommandId, cancellationToken).ConfigureAwait(false);
            if (positive.OutcomePresent)
            { positiveCount++; }
        }
        await Assert.That(positiveCount).IsGreaterThanOrEqualTo(RequestCqrsRf3Protocol.NodeCount / 2 + 1);
    }

    private async Task<C1OutcomeInspectionReceipt> InspectNodeAsync(NodeEpochRf3NodeObservation node,
        Guid commandId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(node.Status.NodeId, out var expectedNodeId) || expectedNodeId == Guid.Empty)
        { throw new InvalidOperationException(MissingCut); }
        var nodeRoot = Path.Combine(dataRoot, node.Name);
        var request = new C1OutcomeInspectionRequest(C1OutcomeInspectionProtocol.Version,
            Path.Combine(nodeRoot, PartitionStoreProtocol.CanonicalDirectory), expectedNodeId,
            node.Status.Incarnation, principalId, commandId);
        var result = await C1OutcomeInspectionProcess.RunAsync(C1OutcomeInspectionProtocol.SerializeRequest(request),
            Path.Combine(nodeRoot, PartitionStoreProtocol.OwnershipFile), cancellationToken).ConfigureAwait(false);
        await AssertJoinedAsync(result).ConfigureAwait(false);
        var receipt = C1OutcomeInspectionProtocol.DeserializeReceipt(result.StandardOutput);
        await Assert.That(receipt.Version).IsEqualTo(C1OutcomeInspectionProtocol.Version);
        await Assert.That(receipt.NodeId).IsEqualTo(expectedNodeId);
        await Assert.That(receipt.Incarnation).IsEqualTo(node.Status.Incarnation);
        await Assert.That(receipt.Incarnation).IsEqualTo(profile.Incarnation);
        await Assert.That(receipt.FormatVersion).IsEqualTo(ExpectedCurrentDataEpoch);
        await Assert.That(receipt.Position).IsGreaterThanOrEqualTo(0L);
        return receipt;
    }

    private static async Task AssertJoinedAsync(C1OutcomeInspectionProcessResult result)
    {
        await Assert.That(result.ProcessId).IsGreaterThan(0);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardOutputExceeded).IsFalse();
        await Assert.That(result.StandardErrorExceeded).IsFalse();
        await Assert.That(result.StandardError).IsEmpty();
        await Assert.That(result.ProcessReaped).IsTrue();
        await Assert.That(result.InputWriterSettled).IsTrue();
        await Assert.That(result.StandardOutputReaderSettled).IsTrue();
        await Assert.That(result.StandardErrorReaderSettled).IsTrue();
        await Assert.That(result.ProcessHandleClosed).IsTrue();
        await Assert.That(result.OuterOwnerReleased).IsTrue();
    }
}
