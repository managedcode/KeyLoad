using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeRetireContinuation
{
    internal static async Task ExecuteAsync(ControlledPartitionMovementNativeHosts owners,
        ControlledPartitionMovementNativeRetireCaptured captured, PartitionMoveRequest request,
        PartitionMoveResult expected, byte[] retained, CommitReceipt seedReceipt, DateTimeOffset recordedAt,
        (CompleteBlobUploadRequest Request, BlobCommitResult<BlobMetadata> Expected) blob,
        CancellationToken cancellationToken)
    {
        await owners.RestartAsync(cancellationToken);
        await ControlledPartitionMovementNativeRegistration.VerifyAsync(owners);
        await ControlledPartitionMovementNativeRetireRetained.ColdAsync(owners, captured, request, retained);
        await ControlledPartitionMovementNativeModels.ReadAsync(owners.Target, recordedAt, cancellationToken);
        await ControlledPartitionMovementNativeBlobAssertions.ReadAsync(owners.Target, blob.Expected.Value, cancellationToken);
        await ControlledPartitionMovementNativeRetireProducer.ExpiredAsync(owners, captured, cancellationToken);
        await ControlledPartitionMovementNativeRetireProducer.CompleteAsync(owners,
            request with { Mode = PartitionMoveMode.Resume }, expected, cancellationToken);
        await OriginalAsync(owners, seedReceipt, recordedAt, blob, cancellationToken);
        var healthy = await ControlledPartitionMovementNativeHealthy.SubmitAsync(owners.Target, cancellationToken);
        await owners.RestartAsync(cancellationToken);
        await ControlledPartitionMovementNativeRegistration.VerifyAsync(owners);
        await ControlledPartitionMovementNativeRetireProducer.CompleteAsync(owners, request, expected, cancellationToken);
        await OriginalAsync(owners, seedReceipt, recordedAt, blob, cancellationToken);
        await ControlledPartitionMovementNativeHealthy.ReplayAsync(owners.Target, healthy, cancellationToken);
    }

    private static async Task OriginalAsync(ControlledPartitionMovementNativeHosts owners,
        CommitReceipt seedReceipt, DateTimeOffset recordedAt,
        (CompleteBlobUploadRequest Request, BlobCommitResult<BlobMetadata> Expected) blob,
        CancellationToken cancellationToken)
    {
        var replay = await owners.Target.SubmitAsync(OperationKind.Batch, ControlledPartitionMovementCorpus.SeedId,
            PhysicalShardCatalogFixture.RootPrincipalId,
            NativeSerialization.Serialize(ControlledPartitionMovementCorpus.Seed()), cancellationToken);
        await ControlledPartitionMovementNativeSeed.ReceiptAsync(replay, seedReceipt);
        await ControlledPartitionMovementNativeModels.ReadAsync(owners.Target, recordedAt, cancellationToken);
        await ControlledPartitionMovementNativeBlobAssertions.ReplayAsync(owners.Target,
            blob.Request, blob.Expected, cancellationToken);
    }
}
