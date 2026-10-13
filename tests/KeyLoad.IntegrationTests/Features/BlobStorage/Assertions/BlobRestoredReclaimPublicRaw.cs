using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.BackupRestore;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobRestoredReclaimPublicRaw
{
    private const string Part = "blob-part-v1";
    private const string Meta = "blob-partmeta-v1";
    private const string Quota = "blob-quota-v1";
    private const string GuidFormat = "N";
    private const string Invalid = "The restored reclamation native rows do not match their acknowledged cursor.";
    internal static void Require(ClusterRestoreRf3Fixture target, BlobRestoredReclaimPublicState state, bool final, CancellationToken token)
    {
        var failures = new List<Exception>();
        var group = BlobRestoredReclaimPublicFlow.Group(target, state);
        for (var index = group * 3; index < group * 3 + 3; index++)
        { Voter(target, state, index, final, failures, token); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static void Voter(ClusterRestoreRf3Fixture target, BlobRestoredReclaimPublicState state,
        int index, bool final, List<Exception> failures, CancellationToken token)
    {
        ZoneTreeStore? store = null;
        ServerFailureObserver.Observe(() =>
        {
            store = ClusterRestoreRf3ExistingTarget.Open(target, index);
            var work = new ReadExecutionBudget(IntegrationExecutionOptions.DatabaseLimits(), cancellationToken: token);
            store.Read(view => { Parts(view, work, state, final); return true; });
        }, failures);
        if (store is not null)
        { ServerFailureObserver.Observe(store.Dispose, failures); }
    }
    private static void Parts(IKeyValueView view, ReadExecutionBudget work, BlobRestoredReclaimPublicState state, bool final)
    {
        var blob = state.Active.Begin.Blob;
        var quota = KeyCodec.Encode(Quota, blob.Partition.TenantId, blob.Partition.DatabaseId, blob.Partition.TransactionDomainId, blob.Resource);
        if (work.Read(view, quota) is not { Length: > 0 })
        { throw new InvalidOperationException(Invalid); }
        for (var ordinal = 0; ordinal < 2; ordinal++)
        {
            var raw = work.Read(view, Key(Part, blob, state.Active.Begin.UploadId, ordinal));
            var metadata = work.Read(view, Key(Meta, blob, state.Active.Begin.UploadId, ordinal));
            var retained = !final && ordinal == 1;
            if (retained && (raw is null || !raw.AsSpan().SequenceEqual(BlobArchiveActivePublicPublication.Bytes(1, 65536)) || metadata is not { Length: > 0 })
                || !retained && (raw is not null || metadata is not null))
            { throw new InvalidOperationException(Invalid); }
        }
    }
    private static byte[] Key(string family, BlobRef blob, Guid upload, int ordinal)
        => KeyCodec.Encode(family, blob.Partition.TenantId, blob.Partition.DatabaseId, blob.Partition.TransactionDomainId,
            blob.Partition.PartitionKey, blob.Resource, blob.Id, upload.ToString(GuidFormat), (long)ordinal);
}
