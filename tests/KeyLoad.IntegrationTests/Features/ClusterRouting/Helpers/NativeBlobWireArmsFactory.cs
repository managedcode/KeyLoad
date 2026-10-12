using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.Server.Features.BlobStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeBlobWireArmsFactory
{
    internal static NativeBlobWireArms Create(NativeBlobWireOwner owner, string ownerDigest,
        RemoteTransferNativeParentSeed seed, NativeBlobWireSeedCommands commands, NativeBlobWireEffectPlan plan)
    {
        var principal = PartitionMovementPublicParentRf3Administrator.InitialDefinition();
        var original = seed.Metadata.Blob;
        if (commands.Begin.Blob != original || commands.Write.Blob != original || commands.Complete.Blob != original
            || commands.Begin.UploadId != commands.Write.UploadId || commands.Begin.UploadId != commands.Complete.UploadId)
        { throw new InvalidOperationException(NativeBlobWireProbeProtocol.InvalidEvidence); }
        var secondary = original with { Id = "wire-secondary" };
        var arms = new NativeBlobWireArm[]
        {
            Outcome(NativeBlobWireIdentity.BeginUpload, OperationKind.BeginBlobUpload, commands.Begin.CommandId, original, commands.Begin.UploadId),
            Outcome(NativeBlobWireIdentity.WritePart, OperationKind.WriteBlobPart, commands.Write.CommandId, original, commands.Write.UploadId),
            Outcome(NativeBlobWireIdentity.CompleteUpload, OperationKind.CompleteBlobUpload, commands.Complete.CommandId, original, commands.Complete.UploadId),
            Outcome(NativeBlobWireIdentity.AbortUpload, OperationKind.AbortBlobUpload, plan.AbortCommandId, secondary, plan.AbortedUploadId),
            Outcome(NativeBlobWireIdentity.Delete, OperationKind.DeleteBlob, plan.DeleteCommandId, secondary, null),
            Outcome(NativeBlobWireIdentity.Reclaim, OperationKind.ReclaimBlob, plan.ReclaimCommandId, secondary, plan.AbortedUploadId),
            Read(NativeBlobWireIdentity.Metadata, ControlledBlobReadPurpose.Metadata, original, new BlobMetadataRequest(original)),
            Read(NativeBlobWireIdentity.UploadInfo, ControlledBlobReadPurpose.UploadInfo, original,
                new BlobUploadInfoRequest(original, seed.Metadata.VersionId!.Value)),
            Read(NativeBlobWireIdentity.Range, ControlledBlobReadPurpose.Range, original,
                new BlobReadRequest(original, seed.Metadata.Revision, 0, 8)),
            Read(NativeBlobWireIdentity.List, ControlledBlobReadPurpose.List,
                original with { Id = BlobListAccumulator.ScopeId }, new BlobListRequest(seed.Partition, original.Resource))
        };
        return new(NativeBlobWireProbeProtocol.Version, owner.SessionId, ownerDigest,
            seed.Request.MoveId, principal.Id, principal.TenantId, principal.PolicyEpoch, seed.Partition,
            original.Resource, ResourceKind.BlobStore, seed.Partition.TransactionDomainId, arms);
    }

    private static NativeBlobWireArm Outcome(NativeBlobWireIdentity identity, OperationKind kind,
        Guid command, BlobRef blob, Guid? upload)
        => new(identity, Guid.NewGuid(), ControlledBlobReadPurpose.Outcome, kind, command, blob.Id, upload, null);

    private static NativeBlobWireArm Read<T>(NativeBlobWireIdentity identity, ControlledBlobReadPurpose purpose, BlobRef blob, T request)
        => new(identity, Guid.NewGuid(), purpose, null, null, blob.Id, null,
            NativeBlobWireCodec.Digest(NativeSerialization.Serialize(request)));
}
