using System.Security.Cryptography;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Owns genuine configured native peer admissions through command refusal, completion and cold continuation.</summary>
internal static class NativeControlledLifetimeCarrierBudgetFlow
{
    private const int KeyBytes = 32;
    private const string WitnessKind = "NativeControlledLifetimeCarrierFit";

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
                await NativeControlledLifetimePayloadBoundary.AssertAsync(source, target, token);
                var state = await ResourceLifetimeGrantStages.AdmitAsync(source, target, listeners, corpus,
                    sourceOptions, targetOptions, sourceAdmission, admission, token);
                var carrier = NativeSerialization.Serialize(new PartitionControlLifetimeTransportV1(
                    NativeSerialization.Serialize(state.Request), state.Proof));
                await Assert.That(carrier.Length).IsLessThanOrEqualTo(NativeControlledLifetimePayloadBoundary.MaximumBytes);
                await Assert.That(source.Database.Limits.MaxBatchBytes).IsEqualTo(NativeControlledLifetimePayloadBoundary.MaximumBytes);
                await Assert.That(target.Database.Limits.MaxBatchBytes).IsEqualTo(NativeControlledLifetimePayloadBoundary.MaximumBytes);
                await Assert.That(sourceOptions.Core.DatabaseLimits.Value.MaxBatchBytes).IsEqualTo(NativeControlledLifetimePayloadBoundary.MaximumBytes);
                await Assert.That(targetOptions.Core.DatabaseLimits.Value.MaxBatchBytes).IsEqualTo(NativeControlledLifetimePayloadBoundary.MaximumBytes);
                await ResourceLifetimeGrantAssertions.CaptureAsync(state);
                await NativeControlledLifetimeLocalSubstitution.ExecuteAsync(state, token);
                var result = await ResourceLifetimeGrantStages.CompleteAsync(state, token);
                await ResourceLifetimeGrantAssertions.CompletedAsync(state, result, token);
                await Console.Out.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(new
                {
                    Kind = WitnessKind,
                    MaximumBytes = NativeControlledLifetimePayloadBoundary.MaximumBytes,
                    CarrierBytes = carrier.Length,
                    SignedCommandBytes = state.Original.NativePayload.Length
                }));
                completed = state;
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await ResourceLifetimeGrantCold.ExecuteAsync(completed
            ?? throw new InvalidOperationException("The original controlled command did not complete."),
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(source.Database.Limits.MaxBatchBytes).IsEqualTo(NativeControlledLifetimePayloadBoundary.MaximumBytes);
        await Assert.That(target.Database.Limits.MaxBatchBytes).IsEqualTo(NativeControlledLifetimePayloadBoundary.MaximumBytes);
    }

    private static PartitionMovementPeerAdmission Open(ControlledPartitionMovementNode owner, ServerRuntimeOptions runtime)
        => new(runtime.Node, owner.Database, runtime.ReplicaConfiguration, runtime.GrainRouting,
            runtime.Membership, runtime.PartitionMovement, owner.Database.EvaluationClock);
}
