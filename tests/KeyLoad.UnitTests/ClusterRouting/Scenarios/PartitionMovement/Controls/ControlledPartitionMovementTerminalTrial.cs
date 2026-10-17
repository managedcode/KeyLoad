using System.Security.Cryptography;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Composes actual configured A/B admissions around the full technical terminal operation.</summary>
internal static class ControlledPartitionMovementTerminalTrial
{
    private const int PeerKeyBytes = 32;

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackListeners listeners,
        ControlledPartitionMovementLoopbackCorpus corpus)
        => await ExecuteCoreAsync(source, target, listeners, corpus, naturalExpiry: false);

    internal static Task ExecuteNaturalExpiryAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackListeners listeners,
        ControlledPartitionMovementLoopbackCorpus corpus)
        => ExecuteCoreAsync(source, target, listeners, corpus, naturalExpiry: true);

    internal static Task ExecuteRetainedOutcomesAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackListeners listeners,
        ControlledPartitionMovementLoopbackCorpus corpus)
        => ExecuteCoreAsync(source, target, listeners, corpus, naturalExpiry: false, new());

    internal static Task ExecuteWithAdditionalSeedAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackListeners listeners,
        ControlledPartitionMovementLoopbackCorpus corpus, Func<CancellationToken, Task> additionalSeed)
        => ExecuteCoreAsync(source, target, listeners, corpus, naturalExpiry: false, additionalSeed: additionalSeed);

    private static async Task ExecuteCoreAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackListeners listeners,
        ControlledPartitionMovementLoopbackCorpus corpus, bool naturalExpiry, ControlledPartitionMovementRetainedOutcomeAudit? audit = null,
        Func<CancellationToken, Task>? additionalSeed = null)
    {
        var options = UnitExecutionOptions.NativeMovementProcess().Value;
        var wholeExpiresAt = source.Database.EvaluationClock.GetUtcNow() + options.OperationTimeout;
        using var deadline = new CancellationTokenSource(options.OperationTimeout, source.Database.EvaluationClock);
        using var original = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        var token = original.Token;
        var initialPosition = source.Store.Position;
        var seeded = await ControlledPartitionMovementPrepareSeed.ExecuteAsync(source, target, corpus, token);
        if (additionalSeed is not null)
        {
            await additionalSeed(token);
        }
        if (audit is not null)
        {
            await audit.CaptureAsync(source, target, token);
        }
        var controlSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(PeerKeyBytes));
        var targetSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(PeerKeyBytes));
        var sourceRuntime = ControlledPartitionMovementLoopbackOptions.Bind(source, corpus,
            controlSecret, targetSecret, control: true);
        var targetRuntime = ControlledPartitionMovementLoopbackOptions.Bind(target, corpus,
            controlSecret, targetSecret, control: false);
        await WithSourceAdmissionAsync(source, sourceRuntime, async sourceAdmission =>
            await WithTargetAdmissionAsync(target, targetRuntime, async targetAdmission =>
                await ControlledPartitionMovementTerminalOperation.ExecuteAsync(source, target, corpus,
                    sourceRuntime, targetRuntime, sourceAdmission, targetAdmission,
                    ControlledPartitionMovementPrepareRequest.CallerAddress(listeners), seeded.Receipt,
                    seeded.Authority, seeded.RecordedAt, initialPosition, wholeExpiresAt, naturalExpiry ? options.IssuedGrantExpiryWindow : TimeSpan.Zero, token, audit)));
        if (audit is not null)
        {
            await audit.FinishAsync(source, target, token);
        }
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
