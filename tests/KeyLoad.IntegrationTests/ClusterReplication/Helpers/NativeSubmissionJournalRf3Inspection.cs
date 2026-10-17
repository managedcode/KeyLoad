using KeyLoad.CrashHost.Features.ClusterReplication;
using KeyLoad.CrashHost.Features.ClusterRouting.Processes;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class NativeSubmissionJournalRf3Inspection
{
    internal static async Task<Guid> RequireAsync(NativeSubmissionFaultRf3Owner owner,
        NodeEpochRf3NodeObservation node, CommandRequest command, RequestCqrsProbeMarkerRecord marker,
        long canonicalPosition, CancellationToken token)
    {
        var profile = owner.Profile ?? throw NativeSubmissionFaultRf3Owner.Missing();
        var identity = owner.Identity ?? throw NativeSubmissionFaultRf3Owner.Missing();
        var request = new C1SubmissionJournalInspectionRequest(C1SubmissionJournalInspectionProtocol.Version,
            Path.Combine(owner.DataRoot, node.Name), Guid.Parse(node.Status.NodeId), profile.Incarnation,
            node.Admin.LocalVoter ?? throw Missing(), [.. node.Admin.Voters], marker.EntryIndex ?? throw Missing(),
            marker.EntryTerm ?? throw Missing());
        var child = await C1OutcomeInspectionProcess.RunSubmissionJournalAsync(
            C1SubmissionJournalInspectionProtocol.SerializeRequest(request),
            Path.Combine(owner.DataRoot, node.Name, "node.owner.lock"), token).ConfigureAwait(false);
        await NativeSubmissionFullOutcomeInspection.RequireJoinedAsync(child).ConfigureAwait(false);
        var receipt = C1SubmissionJournalInspectionProtocol.DeserializeReceipt(child.StandardOutput);
        await NativeSubmissionJournalRf3Assertions.RequireAsync(receipt, request, command, identity,
            marker, canonicalPosition).ConfigureAwait(false);
        return receipt.ReplicaNodeId;
    }

    private static InvalidOperationException Missing()
        => new("The genuine observed retained journal entry identity is missing.");
}
