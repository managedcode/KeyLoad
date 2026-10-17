using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class NativeSubmissionFaultRf3Assertions
{
    internal static NodeEpochRf3NodeObservation Leader(IReadOnlyList<NodeEpochRf3NodeObservation> nodes)
    {
        var leaders = nodes.Select(value => value.Status.Leader).Distinct(StringComparer.Ordinal).ToArray();
        if (leaders.Length != 1 || leaders[0] is null)
        {
            throw NativeSubmissionFaultRf3Owner.Missing();
        }
        return nodes.Single(value => value.Admin.LocalVoter == leaders[0]);
    }

    internal static async Task SourceAsync(RequestCqrsProbeMarkerRecord source, CommandRequest command,
        ReplicaSiloDiscovery nativeLeader)
    {
        await Assert.That(source.RequestId).IsNotEqualTo(Guid.Empty);
        await Assert.That(source.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(source.Phase).IsEqualTo(RequestCqrsProbePhase.BeforeSubmit);
        await Assert.That(source.Outcome).IsEqualTo(RequestCqrsProbeOutcome.Observed);
        await Assert.That(source.Voter).IsEqualTo(nativeLeader.VoterId);
        await Assert.That(source.SiloAddress).IsEqualTo(nativeLeader.SiloAddress);
        await Assert.That(source.EntryIndex).IsNull();
        await Assert.That(source.EntryTerm).IsNull();
    }

    internal static async Task DescendantAsync(RequestCqrsProbeMarkerRecord source,
        RequestCqrsProbeMarkerRecord actual, Guid descendantArm)
    {
        await Assert.That(actual.ArmId).IsEqualTo(descendantArm);
        await Assert.That(actual.ArmId).IsNotEqualTo(source.ArmId);
        await Assert.That(actual.SessionId).IsEqualTo(source.SessionId);
        await Assert.That(actual.RequestId).IsEqualTo(source.RequestId);
        await Assert.That(actual.CommandId).IsEqualTo(source.CommandId);
        await Assert.That(actual.Voter).IsEqualTo(source.Voter);
        await Assert.That(actual.SiloAddress).IsEqualTo(source.SiloAddress);
        await Assert.That(actual.Phase).IsEqualTo(RequestCqrsProbePhase.ReplicaEntryAcknowledged);
        await Assert.That(actual.Outcome).IsEqualTo(RequestCqrsProbeOutcome.FaultRequested);
        await Assert.That(actual.EntryIndex is > 0).IsTrue();
        await Assert.That(actual.EntryTerm is > 0).IsTrue();
    }

    internal static async Task DisposedAsync(RequestCqrsProbeMarkerRecord acknowledged,
        RequestCqrsProbeMarkerRecord disposed)
    {
        await Assert.That(disposed.Phase).IsEqualTo(RequestCqrsProbePhase.ReplicaEntryObserverDisposed);
        await Assert.That(disposed.Outcome).IsEqualTo(RequestCqrsProbeOutcome.Observed);
        await Assert.That(disposed with
        {
            Phase = acknowledged.Phase,
            Outcome = acknowledged.Outcome
        })
            .IsEqualTo(acknowledged);
    }
}
