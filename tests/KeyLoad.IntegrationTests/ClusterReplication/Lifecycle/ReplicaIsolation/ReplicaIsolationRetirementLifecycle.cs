using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal sealed class ReplicaIsolationRetirementLifecycle(ReplicaIsolationBuildPlan plan, string root,
    IReadOnlyDictionary<string, string> names, Dictionary<string, ReplicaIsolationNode> nodes,
    ReplicaIsolationExecution execution)
{
    internal async Task SettleAfterStopAsync(bool stopped, CancellationToken cancellationToken)
    {
        if (!stopped)
        {
            throw new InvalidOperationException("The actual fault topology did not settle; its root and images remain owned.");
        }
        var evidence = new List<ReplicaIsolationRetirementObservation>();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => ReplicaIsolationRetirement.VerifyNodesStoppedAsync(
            names, nodes.Values.ToArray(), evidence, cancellationToken), failures);
        if (failures.Count == ReplicaIsolationFlowProtocol.Zero)
        {
            ServerFailureObserver.Observe(() => ReplicaIsolationRetirement.AssertNamedLocks(root, plan.Targets.Select(target => target.ResourceName), evidence), failures);
        }
        if (failures.Count == ReplicaIsolationFlowProtocol.Zero)
        {
            await ServerFailureObserver.ObserveAsync(() => ReplicaIsolationRetirement.RemoveImagesAsync(plan, evidence, cancellationToken), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => ReplicaIsolationEvidence.WriteRetirementAsync(root, evidence,
            failures.Count, CancellationToken.None), failures);
        await ServerFailureObserver.ObserveAsync(() => ReplicaIsolationEvidence.WriteMutationsAsync(root, execution.Mutations,
            failures, CancellationToken.None), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
