using System.Security.Cryptography;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Owns genuine configured native peer admissions through command refusal, completion and cold continuation.</summary>
internal static class ResourceLifetimeGrantFlow
{
    private const int KeyBytes = 32;

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackListeners listeners,
        ControlledPartitionMovementLoopbackCorpus corpus)
    {
        var controlKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes));
        var targetKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes));
        var sourceOptions = ControlledPartitionMovementLoopbackOptions.Bind(source, corpus, controlKey, targetKey, control: true);
        var targetOptions = ControlledPartitionMovementLoopbackOptions.Bind(target, corpus, controlKey, targetKey, control: false);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var admission = Open(source, sourceOptions);
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
        ResourceLifetimeGrantState? completed = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var admission = Open(target, targetOptions);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var token = TestContext.Current!.Execution.CancellationToken;
                var state = await ResourceLifetimeGrantStages.AdmitAsync(source, target, listeners, corpus,
                    sourceOptions, targetOptions, sourceAdmission, admission, token);
                await ResourceLifetimeGrantAssertions.CaptureAsync(state);
                await ResourceLifetimeGrantRefusals.ExecuteAsync(state, token);
                await ResourceLifetimeGrantStoredAuthority.ExecuteAsync(state, token);
                var result = await ResourceLifetimeGrantStages.CompleteAsync(state, token);
                await ResourceLifetimeGrantAssertions.CompletedAsync(state, result, token);
                completed = state;
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await ResourceLifetimeGrantCold.ExecuteAsync(completed
            ?? throw new InvalidOperationException("The original controlled command did not complete."),
            TestContext.Current!.Execution.CancellationToken);
    }

    private static PartitionMovementPeerAdmission Open(ControlledPartitionMovementNode owner, ServerRuntimeOptions runtime)
        => new(runtime.Node, owner.Database, runtime.ReplicaConfiguration, runtime.GrainRouting,
            runtime.Membership, runtime.PartitionMovement, owner.Database.EvaluationClock);
}
