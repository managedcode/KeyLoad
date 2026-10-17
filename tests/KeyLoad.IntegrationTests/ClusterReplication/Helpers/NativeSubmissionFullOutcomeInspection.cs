using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.ClusterRouting.Processes;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class NativeSubmissionFullOutcomeInspection
{
    internal static async Task RequireAsync(NativeSubmissionFaultRf3Owner owner, CommandRequest command,
        RequestCqrsFaultRetryResult retry, IReadOnlyList<NodeEpochRf3NodeObservation> original, RequestCqrsProbeMarkerRecord observedEntry,
        CancellationToken token)
    {
        var wave = owner.Wave ?? throw NativeSubmissionFaultRf3Owner.Missing();
        var profile = owner.Profile ?? throw NativeSubmissionFaultRf3Owner.Missing();
        var identity = owner.Identity ?? throw NativeSubmissionFaultRf3Owner.Missing();
        await NodeEpochRf3StatusOracle.EventuallyCaughtUpAsync(wave.App, profile,
            retry.SdkReceipt.Token.Position, token).ConfigureAwait(false);
        var current = await NodeEpochRf3StatusOracle.CaptureAsync(wave.App, profile, token).ConfigureAwait(false);
        await NodeEpochRf3StatusOracle.AssertSamePhysicalTopologyAsync(original, current).ConfigureAwait(false);
        await owner.StopForOutcomeInspectionAsync().ConfigureAwait(false);
        var replicaNodeIds = new HashSet<Guid>();
        foreach (var node in current)
        {
            token.ThrowIfCancellationRequested();
            var nodeId = Guid.Parse(node.Status.NodeId);
            var request = new C1SubmissionOutcomeInspectionRequest(C1SubmissionOutcomeInspectionProtocol.Version,
                Path.Combine(owner.DataRoot, node.Name, "database"), nodeId, profile.Incarnation,
                identity.PrincipalId, command.CommandId, command.Partition);
            var child = await C1OutcomeInspectionProcess.RunSubmissionOutcomeAsync(
                C1SubmissionOutcomeInspectionProtocol.SerializeRequest(request),
                Path.Combine(owner.DataRoot, node.Name, "node.owner.lock"), token).ConfigureAwait(false);
            await RequireJoinedAsync(child).ConfigureAwait(false);
            var receipt = C1SubmissionOutcomeInspectionProtocol.DeserializeReceipt(child.StandardOutput);
            await NativeSubmissionFullOutcomeAssertions.RequireAsync(receipt, nodeId,
                profile.Incarnation, identity, command, retry).ConfigureAwait(false);
            var replicaNodeId = await NativeSubmissionJournalRf3Inspection.RequireAsync(owner, node, command,
                observedEntry, receipt.Position, token).ConfigureAwait(false);
            await Assert.That(replicaNodeIds.Add(replicaNodeId)).IsTrue();
        }
    }

    internal static async Task RequireJoinedAsync(C1OutcomeInspectionProcessResult child)
    {
        await Assert.That(child.ExitCode).IsEqualTo(0);
        await Assert.That(child.ProcessId > 0).IsTrue();
        await Assert.That(child.StandardOutputExceeded).IsFalse();
        await Assert.That(child.StandardErrorExceeded).IsFalse();
        await Assert.That(child.StandardOutput.Length <= C1SubmissionOutcomeInspectionProtocol.MaximumReceiptBytes).IsTrue();
        await Assert.That(child.StandardError.Length).IsEqualTo(0);
        await Assert.That(child.ProcessReaped).IsTrue();
        await Assert.That(child.InputWriterSettled).IsTrue();
        await Assert.That(child.StandardOutputReaderSettled).IsTrue();
        await Assert.That(child.StandardErrorReaderSettled).IsTrue();
        await Assert.That(child.ProcessHandleClosed).IsTrue();
        await Assert.That(child.OuterOwnerReleased).IsTrue();
    }
}
