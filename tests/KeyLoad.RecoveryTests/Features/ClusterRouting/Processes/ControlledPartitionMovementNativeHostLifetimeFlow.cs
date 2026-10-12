using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeHostLifetimeFlow
{
    private const int FirstOwner = 0;
    private const int Owners = 6;
    private const string NodePrefix = "node";
    private const string RetainedRoot = "KeyLoad.ControlledMovement.NativeRetainedRoot";

    internal static async Task RunAsync(IOptions<NativeProcessReadinessOptions> readiness,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        using var admission = await StorageTrialLease.AcquireAsync(cancellationToken);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(
            CommandIdempotencyProcessRecoveryTests.RunTimeoutSeconds), clock);
        using var token = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        ControlledPartitionMovementNativeHosts? hosts = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await ControlledPartitionMovementNativeFailedCreationFlow.RunAsync(readiness, clock, token.Token);
            hosts = await ControlledPartitionMovementNativeHosts.CreateAsync(token.Token);
            var prepared = await ControlledPartitionMovementNativeProcessOperation.WarmAsync(hosts, token.Token);
            hosts.RetainRoots();
            await CancelAndJoinAsync(hosts);
            await LocksAsync(hosts.Root, readiness, clock, token.Token);
            hosts = await ControlledPartitionMovementNativeHosts.OpenAsync(prepared.Input.Hosts, hosts.Root, token.Token);
            await ContinuationAsync(hosts, prepared, token.Token);
        }, failures);
        if (hosts is not null)
        {
            if (failures.Count != 0)
            { hosts.RetainRoots(); }
            await ServerFailureObserver.ObserveAsync(() => hosts.DisposeAsync().AsTask(), failures);
            if (failures.Count != 0)
            { foreach (var failure in failures) { failure.Data[RetainedRoot] = hosts.Root; } }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CancelAndJoinAsync(ControlledPartitionMovementNativeHosts hosts)
    {
        using var selectedCaller = new CancellationTokenSource();
        await selectedCaller.CancelAsync().ConfigureAwait(false);
        var initiating = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => hosts.StopAsync(selectedCaller.Token), initiating);
        await Assert.That(initiating.Count).IsGreaterThan(FirstOwner);
        foreach (var failure in initiating)
        { await Assert.That(IsCancellation(failure)).IsTrue(); }
        var closing = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => hosts.CloseRetainingRootAsync().AsTask(), closing);
        await Assert.That(closing.Count).IsGreaterThan(FirstOwner);
        foreach (var failure in closing)
        { await Assert.That(IsCancellation(failure)).IsTrue(); }
        await Assert.That(hosts.Nodes.Count).IsEqualTo(Owners);
        foreach (var host in hosts.Nodes)
        { await Assert.That(host.IsJoined).IsTrue(); }
        await Assert.That(Directory.Exists(hosts.Root)).IsTrue();
    }

    private static bool IsCancellation(Exception error)
        => error is OperationCanceledException
            || error is AggregateException aggregate
                && aggregate.InnerExceptions.Count != FirstOwner
                && aggregate.InnerExceptions.All(IsCancellation);

    private static async Task ContinuationAsync(ControlledPartitionMovementNativeHosts hosts,
        ControlledPartitionMovementNativeProcessPrepared original, CancellationToken cancellationToken)
    {
        var before = await hosts.CaptureAsync(cancellationToken);
        await ControlledPartitionMovementNativeProcessColdAssertions.ColdAsync(original.Before, before);
        var observed = await ControlledPartitionMovementNativeProcessOutcome.ReadAsync(hosts, original.Input.Original, cancellationToken);
        await ControlledPartitionMovementNativeProcessAssertions.OutcomeAsync(observed.OriginalBytes, original.Phase);
        var source = hosts.Source;
        var seed = await source.SubmitAsync(OperationKind.Batch, original.Seed.CommandId,
            ControlledPartitionMovementProcessLiteralCorpus.PrincipalId,
            NativeSerialization.Serialize(ControlledPartitionMovementProcessLiteralCorpus.Seed()), cancellationToken);
        await ControlledPartitionMovementNativeProcessAssertions.ReplyAsync(seed, original.Seed);
        var blob = await source.SubmitAsync(OperationKind.CompleteBlobUpload, original.BlobRequest.CommandId,
            ControlledPartitionMovementProcessLiteralCorpus.PrincipalId, NativeSerialization.Serialize(original.BlobRequest), cancellationToken);
        await ControlledPartitionMovementNativeProcessAssertions.ReplyAsync(blob, original.Blob);
        await ControlledPartitionMovementProcessModelAssertions.ReadAsync(source, original.RecordedAt, source.Store.Position, cancellationToken);
        await ControlledPartitionMovementProcessBlobAssertions.ReadAsync(source, original.Blob.Value, cancellationToken);
        await ControlledPartitionMovementNativeProcessColdAssertions.ExactAsync(before, await hosts.CaptureAsync(cancellationToken));
        var healthy = await ControlledPartitionMovementNativeProcessAssertions.HealthyAsync(source, null, cancellationToken);
        var settled = await hosts.CaptureAsync(cancellationToken);
        await ControlledPartitionMovementNativeProcessAssertions.HealthyAsync(source, healthy, cancellationToken);
        await ControlledPartitionMovementNativeProcessColdAssertions.ExactAsync(settled, await hosts.CaptureAsync(cancellationToken));
    }

    private static async Task LocksAsync(string root, IOptions<NativeProcessReadinessOptions> readiness,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        for (var index = FirstOwner; index < Owners; index++)
        {
            var node = Path.Combine(root, NodePrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await ControlledPartitionMovementProcessOwnership.RequireAsync(node, readiness, clock, cancellationToken);
            using var ownership = new FileStream(Path.Combine(node, PartitionStoreProtocol.OwnershipFile),
                FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
    }
}
