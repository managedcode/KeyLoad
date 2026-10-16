using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.BlobStorage;

internal sealed class RemoteBlobReadRouter(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> options, IOptions<DatabaseLimits> limits, RemoteDocumentClient client,
    RemoteDocumentWorkOwner owner, TimeProvider clock) : IRemoteBlobReadRouter
{
    public async Task<object?> ReadAsync(GrainRequestEnvelope envelope, PrincipalRecord principal,
        ReadOnlyMemory<byte> payload, CancellationToken cancellationToken, IResourceLifetimeWorkBinding binding)
    {
        object? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var operation = owner.Acquire(envelope.RequestId);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var expiry = OriginalExpiry(envelope.ExpiresAt);
                using var original = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
                    operation.ShutdownToken, expiry.Token);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var purpose = RequirePurpose(envelope.ReadKind);
                    var bytes = NativeRequest(purpose, payload);
                    var work = new ReadExecutionBudget(limits, clock, original.Token);
                    var read = new ControlledBlobSourceRead(node, partition, options, limits, client, clock);
                    var controlled = await read.TryReadAsync(envelope, principal.Id, purpose, bytes, work, original.Token, binding).ConfigureAwait(false);
                    result = controlled.Routed ? controlled.Value : ReadLocal(purpose, principal.Id, payload, work, binding);
                }, failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }

    private object? ReadLocal(ControlledBlobReadPurpose purpose, string principal, ReadOnlyMemory<byte> payload,
        ReadExecutionBudget work, IResourceLifetimeWorkBinding binding)
    {
        var blobs = new BlobStorageOperations(partition.Database);
        return purpose switch
        {
            ControlledBlobReadPurpose.Metadata => blobs.MetadataBound(principal, GrainNativePayload.Read<BlobMetadataRequest>(payload), work, binding),
            ControlledBlobReadPurpose.UploadInfo => blobs.UploadInfoBound(principal, GrainNativePayload.Read<BlobUploadInfoRequest>(payload), work, binding),
            ControlledBlobReadPurpose.Range => blobs.ReadBound(principal, GrainNativePayload.Read<BlobReadRequest>(payload), work, binding),
            ControlledBlobReadPurpose.List => blobs.ListBound(principal, GrainNativePayload.Read<BlobListRequest>(payload), work, binding),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteDocumentProtocol.Unavailable)
        };
    }

    private static ReadOnlyMemory<byte> NativeRequest(ControlledBlobReadPurpose purpose, ReadOnlyMemory<byte> payload)
        => purpose switch
        {
            ControlledBlobReadPurpose.Metadata => NativeSerialization.Serialize(GrainNativePayload.Read<BlobMetadataRequest>(payload)),
            ControlledBlobReadPurpose.UploadInfo => NativeSerialization.Serialize(GrainNativePayload.Read<BlobUploadInfoRequest>(payload)),
            ControlledBlobReadPurpose.Range => NativeSerialization.Serialize(GrainNativePayload.Read<BlobReadRequest>(payload)),
            ControlledBlobReadPurpose.List => NativeSerialization.Serialize(GrainNativePayload.Read<BlobListRequest>(payload)),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteDocumentProtocol.Unavailable)
        };

    private static ControlledBlobReadPurpose RequirePurpose(GrainReadKind? kind) => kind switch
    {
        GrainReadKind.BlobMetadata => ControlledBlobReadPurpose.Metadata,
        GrainReadKind.BlobUploadInfo => ControlledBlobReadPurpose.UploadInfo,
        GrainReadKind.BlobRange => ControlledBlobReadPurpose.Range,
        GrainReadKind.BlobList => ControlledBlobReadPurpose.List,
        _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteDocumentProtocol.Unavailable)
    };

    private CancellationTokenSource OriginalExpiry(DateTimeOffset expiresAt)
    {
        var remaining = expiresAt - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteDocumentProtocol.InvalidProof);
        }
        return new(remaining, clock);
    }
}
