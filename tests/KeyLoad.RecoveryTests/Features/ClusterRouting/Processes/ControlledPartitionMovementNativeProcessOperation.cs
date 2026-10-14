using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeProcessOperation
{
    private const string RetainedRoot = "KeyLoad.ControlledMovement.NativeRetainedRoot";
    private const int FirstOwner = 0;
    private const int OwnerCount = 6;
    private const int OriginalReplyCount = 4;
    private const string NodePrefix = "node";

    internal static async Task RunAsync(IOptions<NativeProcessReadinessOptions> readiness,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        using var admission = await StorageTrialLease.AcquireAsync(cancellationToken);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(
            CommandIdempotencyProcessRecoveryTests.RunTimeoutSeconds), clock);
        using var original = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        ControlledPartitionMovementNativeHosts? owners = null;
        string? root = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            owners = await ControlledPartitionMovementNativeHosts.CreateAsync(original.Token);
            root = owners.CaptureInput().Root;
            var prepared = await WarmAsync(owners, original.Token);
            await ControlledPartitionMovementProcessFiles.WriteAsync(root,
                ControlledPartitionMovementNativeProcessProtocol.InputFile, prepared.Input, original.Token);
            await owners.CloseRetainingRootAsync();
            await RequireRootsAsync(root, readiness, clock, original.Token);
            await ControlledPartitionMovementNativeProcessTrial.RunAsync(root, original.Token);
            await RequireRootsAsync(root, readiness, clock, original.Token);
            var result = await ControlledPartitionMovementProcessFiles.ReadAsync<ControlledPartitionMovementNativeProcessResult>(
                root, ControlledPartitionMovementNativeProcessProtocol.ResultFile, original.Token);
            await ControlledPartitionMovementNativeProcessColdAssertions.ColdAsync(prepared.Before, result.Before);
            await ControlledPartitionMovementNativeProcessColdAssertions.ExactAsync(result.Before, result.After);
            await Assert.That(string.IsNullOrWhiteSpace(result.Signature)).IsFalse();
            await ControlledPartitionMovementNativeProcessAssertions.OutcomeAsync(result.OriginalReply, prepared.Phase);
            owners = await ControlledPartitionMovementNativeHosts.OpenAsync(prepared.Input.Hosts, root, original.Token);
            await ColdAsync(owners, prepared, result, original.Token);
            await owners.CloseRetainingRootAsync();
        }, failures);
        await CloseAsync(owners, root, readiness, clock, failures);
    }

    private static async Task CloseAsync(ControlledPartitionMovementNativeHosts? owners, string? root,
        IOptions<NativeProcessReadinessOptions> readiness, TimeProvider clock, List<Exception> failures)
    {
        if (owners is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => owners.CloseRetainingRootAsync().AsTask(), failures);
        }
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(
            CommandIdempotencyProcessRecoveryTests.CleanupTimeoutSeconds), clock);
        if (root is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => RequireRootsAsync(root, readiness, clock, cleanup.Token), failures);
            if (failures.Count == 0)
            {
                ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures);
            }
            foreach (var failure in failures)
            {
                failure.Data[RetainedRoot] = root;
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task<ControlledPartitionMovementNativeProcessPrepared> WarmAsync(
        ControlledPartitionMovementNativeHosts owners, CancellationToken cancellationToken)
    {
        var replies = await ControlledPartitionMovementNativeProcessSeed.SubmitAsync(owners.Source, cancellationToken);
        await Assert.That(replies.Length).IsEqualTo(OriginalReplyCount);
        foreach (var reply in replies)
        {
            await Assert.That(reply.Error).IsNull();
        }
        var seed = await ControlledPartitionMovementNativeProcessAssertions.SeedAsync(owners.Source, replies[^1], cancellationToken);
        var blobReplies = await ControlledPartitionMovementNativeProcessBlob.PublishAsync(owners.Source, cancellationToken);
        await Assert.That(blobReplies.Replies.Length).IsEqualTo(OriginalReplyCount);
        var blobRequest = blobReplies.Request ?? throw new InvalidOperationException(ControlledPartitionMovementNativeProcessProtocol.Invalid);
        var blob = await ControlledPartitionMovementNativeProcessAssertions.BlobAsync(owners.Source,
            blobRequest, blobReplies.Replies[^1], cancellationToken);
        var request = ControlledPartitionMovementNativeProcessPrepare.Create(owners);
        await ControlledPartitionMovementNativeProcessRefusal.RunAsync(owners, request, cancellationToken);
        var replyPhase = await ControlledPartitionMovementNativeProcessPrepare.SendAsync(owners, request, cancellationToken);
        var phase = await ControlledPartitionMovementNativeProcessAssertions.PreparedAsync(owners, request, replyPhase, cancellationToken);
        return new(new(owners.CaptureInput(), request), await owners.CaptureAsync(cancellationToken),
            seed.Seed, seed.RecordedAt, blobRequest, blob, phase);
    }

    private static async Task ColdAsync(ControlledPartitionMovementNativeHosts owners,
        ControlledPartitionMovementNativeProcessPrepared prepared, ControlledPartitionMovementNativeProcessResult process,
        CancellationToken cancellationToken)
    {
        var before = await owners.CaptureAsync(cancellationToken);
        await ControlledPartitionMovementNativeProcessColdAssertions.ColdAsync(process.After, before);
        var outcome = await ControlledPartitionMovementNativeProcessOutcome.ReadAsync(owners, prepared.Input.Original, cancellationToken);
        await ControlledPartitionMovementNativeProcessAssertions.OutcomeAsync(outcome.OriginalBytes, prepared.Phase);
        var source = owners.Source;
        var seed = await source.SubmitAsync(OperationKind.Batch, prepared.Seed.CommandId,
            ControlledPartitionMovementProcessLiteralCorpus.PrincipalId,
            NativeSerialization.Serialize(ControlledPartitionMovementProcessLiteralCorpus.Seed()), cancellationToken);
        await ControlledPartitionMovementNativeProcessAssertions.ReplyAsync(seed, prepared.Seed);
        var blob = await source.SubmitAsync(OperationKind.CompleteBlobUpload, prepared.BlobRequest.CommandId,
            ControlledPartitionMovementProcessLiteralCorpus.PrincipalId, NativeSerialization.Serialize(prepared.BlobRequest), cancellationToken);
        await ControlledPartitionMovementNativeProcessAssertions.ReplyAsync(blob, prepared.Blob);
        await ControlledPartitionMovementProcessModelAssertions.ReadAsync(source, prepared.RecordedAt, source.Store.Position, cancellationToken);
        await ControlledPartitionMovementProcessBlobAssertions.ReadAsync(source, prepared.Blob.Value, cancellationToken);
        await ControlledPartitionMovementNativeProcessColdAssertions.ExactAsync(before, await owners.CaptureAsync(cancellationToken));
        var healthy = await ControlledPartitionMovementNativeProcessAssertions.HealthyAsync(source, null, cancellationToken);
        var settled = await owners.CaptureAsync(cancellationToken);
        await ControlledPartitionMovementNativeProcessAssertions.HealthyAsync(source, healthy, cancellationToken);
        await ControlledPartitionMovementNativeProcessColdAssertions.ExactAsync(settled, await owners.CaptureAsync(cancellationToken));
        await owners.RestartAsync(cancellationToken);
        await ControlledPartitionMovementNativeProcessColdAssertions.ColdAsync(settled, await owners.CaptureAsync(cancellationToken));
        await ControlledPartitionMovementNativeProcessAssertions.HealthyAsync(owners.Source, healthy, cancellationToken);
        await ControlledPartitionMovementProcessModelAssertions.ReadAsync(owners.Source, prepared.RecordedAt, owners.Source.Store.Position, cancellationToken);
        await ControlledPartitionMovementProcessBlobAssertions.ReadAsync(owners.Source, prepared.Blob.Value, cancellationToken);
    }

    private static async Task RequireRootsAsync(string root, IOptions<NativeProcessReadinessOptions> readiness,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        for (var index = FirstOwner; index < OwnerCount; index++)
        {
            var node = Path.Combine(root, NodePrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await ControlledPartitionMovementProcessOwnership.RequireAsync(node, readiness, clock, cancellationToken);
            using var ownership = new FileStream(Path.Combine(node, PartitionStoreProtocol.OwnershipFile),
                FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
    }
}
