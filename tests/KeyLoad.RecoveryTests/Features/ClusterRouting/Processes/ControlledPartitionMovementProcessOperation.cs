using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

/// <summary>Owns the original whole-operation deadline, joined producer handoff, both physical roots and four actual children.</summary>
internal static class ControlledPartitionMovementProcessOperation
{
    private const string Prefix = "keyload-movement-phase-process-";
    private const string SourceDirectory = "source";
    private const string TargetDirectory = "target";
    private const string RetainedRoot = "KeyLoad.ControlledMovement.RetainedRoot";
    private const string GuidFormat = "N";

    internal static async Task RunAsync(CommitStage cut, IOptions<NativeProcessReadinessOptions> readiness,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString(GuidFormat));
        var sourceRoot = Path.Combine(root, SourceDirectory);
        var targetRoot = Path.Combine(root, TargetDirectory);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(
            CommandIdempotencyProcessRecoveryTests.RunTimeoutSeconds), clock);
        using var original = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var (owners, prepared) = await PrepareAsync(sourceRoot, targetRoot, cut, original.Token);
            await ControlledPartitionMovementProcessOwnership.RequireAsync(sourceRoot, readiness, clock, original.Token);
            await ControlledPartitionMovementProcessOwnership.RequireAsync(targetRoot, readiness, clock, original.Token);
            var evidence = new ControlledPartitionMovementProcessReadback(sourceRoot, targetRoot, owners, prepared);
            await ControlledPartitionMovementProcessTrial.RunAsync(sourceRoot, evidence.ObserveAsync, readiness, clock, original.Token);
            await HealthyAsync(sourceRoot, owners, prepared, original.Token);
            await ControlledPartitionMovementProcessColdHealthy.ExecuteAsync(sourceRoot, targetRoot, owners,
                prepared, original.Token);
        }, failures);
        await CleanupAsync(root, sourceRoot, targetRoot, readiness, clock, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<(ControlledPartitionMovementProcessOwners Owners,
        ControlledPartitionMovementProcessPrepared Prepared)> PrepareAsync(string sourceRoot,
        string targetRoot, CommitStage cut, CancellationToken cancellationToken)
    {
        (ControlledPartitionMovementProcessOwners Owners,
            ControlledPartitionMovementProcessPrepared Prepared)? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var listeners = new ControlledPartitionMovementLoopbackListeners();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var owners = new ControlledPartitionMovementProcessOwners(listeners);
                var prepared = await ControlledPartitionMovementProcessProducer.PrepareAsync(sourceRoot,
                    targetRoot, listeners, owners, cut, cancellationToken);
                result = (owners, prepared);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(ControlledPartitionMovementProcessProtocol.Invalid);
    }

    private static async Task HealthyAsync(string sourceRoot, ControlledPartitionMovementProcessOwners owners,
        ControlledPartitionMovementProcessPrepared prepared, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var source = new ControlledPartitionMovementNativeNode(sourceRoot, owners.Control.Owner);
            await ServerFailureObserver.ObserveAsync(() => ControlledPartitionMovementProcessHealthy.ExecuteAsync(
                source, owners, prepared, cancellationToken), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CleanupAsync(string root, string sourceRoot, string targetRoot,
        IOptions<NativeProcessReadinessOptions> readiness, TimeProvider clock, List<Exception> failures)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(
            CommandIdempotencyProcessRecoveryTests.CleanupTimeoutSeconds), clock);
        foreach (var ownerRoot in new[] { sourceRoot, targetRoot })
        {
            await ServerFailureObserver.ObserveAsync(
                () => ControlledPartitionMovementProcessOwnership.RequireAsync(ownerRoot, readiness, clock, cleanup.Token), failures);
        }
        if (failures.Count == 0)
        { ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures); }
        foreach (var failure in failures)
        { failure.Data[RetainedRoot] = root; }
    }
}
