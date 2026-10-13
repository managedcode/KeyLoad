using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.BackupRestore;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobArchiveActivePublicRaw
{
    private const string PartFamily = "blob-part-v1";
    private const string UploadFormat = "N";
    private const string MissingPart = "The restored abandoned blob part is missing.";
    private const string ChangedPart = "The restored abandoned blob part bytes changed.";
    internal static void Require(ClusterRestoreRf3Fixture target, BlobArchiveActivePublicState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        var group = BlobArchiveActivePublicCold.Group(target, state);
        if (group is < 0 or > 1)
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        for (var index = group * 3; index < group * 3 + 3; index++)
        {
            RequireVoter(target, state, index, failures, token);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static void RequireVoter(ClusterRestoreRf3Fixture target, BlobArchiveActivePublicState state,
        int index, List<Exception> failures, CancellationToken token)
    {
        ZoneTreeStore? store = null;
        ServerFailureObserver.Observe(() =>
        {
            store = ClusterRestoreRf3ExistingTarget.Open(target, index);
            var work = new ReadExecutionBudget(IntegrationExecutionOptions.DatabaseLimits(), cancellationToken: token);
            store.Read(view =>
            {
                foreach (var part in state.Active.Parts)
                {
                    RequirePart(view, work, part);
                }
                return true;
            });
        }, failures);
        if (store is not null)
        { ServerFailureObserver.Observe(store.Dispose, failures); }
    }

    private static void RequirePart(IKeyValueView view, ReadExecutionBudget work, WriteBlobPartRequest part)
    {
        var blob = part.Blob;
        var key = KeyCodec.Encode(PartFamily, blob.Partition.TenantId, blob.Partition.DatabaseId,
            blob.Partition.TransactionDomainId, blob.Partition.PartitionKey, blob.Resource, blob.Id,
            part.UploadId.ToString(UploadFormat), (long)part.Ordinal);
        var actual = work.Read(view, key) ?? throw Errors.Fail(ErrorCode.Corruption, MissingPart);
        if (!actual.AsSpan().SequenceEqual(BlobArchiveActivePublicPublication.Bytes(part.Ordinal, 65536)))
        { throw Errors.Fail(ErrorCode.Corruption, ChangedPart); }
    }

}
