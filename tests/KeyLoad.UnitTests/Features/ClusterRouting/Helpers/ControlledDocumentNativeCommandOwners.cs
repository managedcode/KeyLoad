using System.Security.Cryptography;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Owns actual configured peer admissions throughout the full protected command operation.</summary>
internal static class ControlledDocumentNativeCommandOwners
{
    private const int PeerKeyBytes = 32;

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackListeners listeners,
        ControlledPartitionMovementLoopbackCorpus corpus)
    {
        var controlKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(PeerKeyBytes));
        var targetKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(PeerKeyBytes));
        var sourceOptions = ControlledPartitionMovementLoopbackOptions.Bind(source, corpus,
            controlKey, targetKey, control: true);
        var targetOptions = ControlledPartitionMovementLoopbackOptions.Bind(target, corpus,
            controlKey, targetKey, control: false);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var admission = new PartitionMovementPeerAdmission(sourceOptions.Node, source.Database,
                sourceOptions.ReplicaConfiguration, sourceOptions.GrainRouting, sourceOptions.Membership,
                sourceOptions.PartitionMovement, source.Database.EvaluationClock);
            await ServerFailureObserver.ObserveAsync(() => WithTargetAsync(source, target, listeners, corpus,
                sourceOptions, targetOptions, admission), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WithTargetAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackListeners listeners,
        ControlledPartitionMovementLoopbackCorpus corpus, ServerRuntimeOptions sourceOptions,
        ServerRuntimeOptions targetOptions, PartitionMovementPeerAdmission sourceAdmission)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var admission = new PartitionMovementPeerAdmission(targetOptions.Node, target.Database,
                targetOptions.ReplicaConfiguration, targetOptions.GrainRouting, targetOptions.Membership,
                targetOptions.PartitionMovement, target.Database.EvaluationClock);
            await ServerFailureObserver.ObserveAsync(() => ControlledDocumentNativeCommandOperation.ExecuteAsync(
                source, target, listeners, corpus, sourceOptions, targetOptions, sourceAdmission, admission), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
