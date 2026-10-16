using System.Security.Cryptography;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementQuiescenceAdmissions
{
    private const int PeerKeyBytes = 32;

    internal static async Task RunAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        ControlledPartitionMovementLoopbackCorpus corpus,
        Func<ServerRuntimeOptions, ServerRuntimeOptions, PartitionMovementPeerAdmission, PartitionMovementPeerAdmission, Task> operation)
    {
        var controlSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(PeerKeyBytes));
        var targetSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(PeerKeyBytes));
        var sourceRuntime = ControlledPartitionMovementLoopbackOptions.Bind(source, corpus, controlSecret, targetSecret, control: true);
        var targetRuntime = ControlledPartitionMovementLoopbackOptions.Bind(target, corpus, controlSecret, targetSecret, control: false);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var sourceAdmission = Open(source, sourceRuntime);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var targetAdmission = Open(target, targetRuntime);
                await ServerFailureObserver.ObserveAsync(() => operation(sourceRuntime, targetRuntime,
                    sourceAdmission, targetAdmission), failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static PartitionMovementPeerAdmission Open(ControlledPartitionMovementNode owner, ServerRuntimeOptions runtime)
        => new(runtime.Node, owner.Database, runtime.ReplicaConfiguration, runtime.GrainRouting, runtime.Membership,
            runtime.PartitionMovement, owner.Database.EvaluationClock);
}
