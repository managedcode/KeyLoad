using System.Security.Cryptography;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Composes actual configured A/B admissions around the active-capture abort operation.</summary>
internal static class ControlledPartitionMovementAbortTrial
{
    private const int PeerKeyBytes = 32;

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackListeners listeners,
        ControlledPartitionMovementLoopbackCorpus corpus)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var initialPosition = source.Store.Position;
        var seeded = await ControlledPartitionMovementPrepareSeed.ExecuteAsync(source, target, corpus, token);
        var controlSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(PeerKeyBytes));
        var targetSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(PeerKeyBytes));
        var sourceRuntime = ControlledPartitionMovementLoopbackOptions.Bind(source, corpus,
            controlSecret, targetSecret, control: true);
        var targetRuntime = ControlledPartitionMovementLoopbackOptions.Bind(target, corpus,
            controlSecret, targetSecret, control: false);
        await WithSourceAdmissionAsync(source, sourceRuntime, async sourceAdmission =>
            await WithTargetAdmissionAsync(target, targetRuntime, async targetAdmission =>
                await ControlledPartitionMovementAbortOperation.ExecuteAsync(source, target, corpus,
                    sourceRuntime, targetRuntime, sourceAdmission, targetAdmission,
                    ControlledPartitionMovementPrepareRequest.CallerAddress(listeners), seeded.Receipt,
                    seeded.Authority, seeded.Blob, seeded.RecordedAt, initialPosition, token)));
    }

    private static async Task WithSourceAdmissionAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, Func<PartitionMovementPeerAdmission, Task> operation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var admission = new PartitionMovementPeerAdmission(runtime.Node, source.Database,
                runtime.ReplicaConfiguration, runtime.GrainRouting, runtime.Membership,
                runtime.PartitionMovement, source.Database.EvaluationClock);
            await ServerFailureObserver.ObserveAsync(() => operation(admission), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WithTargetAdmissionAsync(ControlledPartitionMovementNode target,
        ServerRuntimeOptions runtime, Func<PartitionMovementPeerAdmission, Task> operation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var admission = new PartitionMovementPeerAdmission(runtime.Node, target.Database,
                runtime.ReplicaConfiguration, runtime.GrainRouting, runtime.Membership,
                runtime.PartitionMovement, target.Database.EvaluationClock);
            await ServerFailureObserver.ObserveAsync(() => operation(admission), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
