using System.Collections.Immutable;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Execution;

internal static class PartitionMoveBlobImport
{
    private const int EmptyCounter = 0;
    private const int SingleCounter = 1;

    internal static byte[] Value(PartitionMoveImagePage page, KeyValueRecord record,
        Guid source, Guid destination)
    {
        if (page.Family == PartitionRecordFamilies.BlobHead)
        {
            var blob = BlobKeys.DecodeScope(record.Key.Span, BlobKeys.HeadSpace, BlobKeys.ScopeComponents);
            var head = BlobRecordReader.Decode<BlobHead>(record.Value.Span);
            BlobRecordReader.ValidateHead(head, blob, source);
            return NativeSerialization.Serialize(head with { Incarnation = destination });
        }
        if (page.Family == PartitionRecordFamilies.BlobState)
        {
            var blob = BlobKeys.DecodeScope(record.Key.Span, BlobKeys.StateSpace, BlobKeys.StateComponents);
            var state = BlobRecordReader.Decode<BlobState>(record.Value.Span);
            if (!record.Key.Span.SequenceEqual(BlobKeys.State(blob, state.UploadId)))
            { throw BlobErrors.Corruption(); }
            BlobRecordReader.ValidateState(state, blob, state.UploadId, source);
            return NativeSerialization.Serialize(state with { Incarnation = destination });
        }
        return record.Value.ToArray();
    }

    internal static void Charge(IAtomicTransaction transaction, PartitionMoveImagePage page,
        KeyValueRecord record, PartitionMoveImageDescriptor descriptor, Guid destination, DatabaseLimits limits)
    {
        if (page.Family == PartitionRecordFamilies.BlobHead)
        {
            var blob = BlobKeys.DecodeScope(record.Key.Span, BlobKeys.HeadSpace, BlobKeys.ScopeComponents);
            Change(transaction, blob, descriptor, destination, limits.MaxScanRecords, EmptyCounter, SingleCounter, EmptyCounter, EmptyCounter);
        }
        else if (page.Family == PartitionRecordFamilies.BlobState)
        {
            var state = BlobRecordReader.Decode<BlobState>(record.Value.Span);
            Change(transaction, state.Blob, descriptor, destination, limits.MaxScanRecords, state.ChargedBytes, EmptyCounter,
                SingleCounter, state.Status == BlobUploadStatus.Active ? SingleCounter : EmptyCounter);
        }
    }

    internal static void Release(IAtomicTransaction transaction, string family, KeyValueRecord record,
        ImmutableArray<ResourceDefinition> resources, Guid incarnation)
    {
        BlobRef blob;
        long bytes = EmptyCounter;
        var keys = EmptyCounter;
        var versions = EmptyCounter;
        var uploads = EmptyCounter;
        if (family == PartitionRecordFamilies.BlobHead)
        {
            blob = BlobKeys.DecodeScope(record.Key.Span, BlobKeys.HeadSpace, BlobKeys.ScopeComponents);
            BlobRecordReader.ValidateHead(BlobRecordReader.Decode<BlobHead>(record.Value.Span), blob, incarnation);
            keys = -SingleCounter;
        }
        else if (family == PartitionRecordFamilies.BlobState)
        {
            var state = BlobRecordReader.Decode<BlobState>(record.Value.Span);
            blob = state.Blob;
            BlobRecordReader.ValidateState(state, blob, state.UploadId, incarnation);
            bytes = checked(-state.ChargedBytes);
            versions = -SingleCounter;
            uploads = state.Status == BlobUploadStatus.Active ? -SingleCounter : EmptyCounter;
        }
        else
        { return; }
        var resource = resources.SingleOrDefault(value => value.Name == blob.Resource
            && value.TransactionDomainId == blob.Partition.TransactionDomainId)
            ?? throw BlobErrors.Corruption();
        BlobQuotaOperations.ValidatePolicy(resource);
        BlobQuotaOperations.Change(transaction, blob, incarnation, resource.BlobPolicy ?? new BlobPolicy(),
            bytes, keys, versions, uploads);
    }

    private static void Change(IAtomicTransaction transaction, BlobRef blob,
        PartitionMoveImageDescriptor descriptor, Guid destination, int maximumCatalogRecords, long bytes, int keys, int versions, int uploads)
    {
        var resource = descriptor.Resources.SingleOrDefault(value => value.Name == blob.Resource
            && value.TransactionDomainId == blob.Partition.TransactionDomainId)
            ?? throw BlobErrors.Corruption();
        if (resource.Kind != ResourceKind.BlobStore)
        { throw BlobErrors.Corruption(); }
        BlobQuotaOperations.ValidatePolicy(resource);
        if (BlobRecordReader.Get<BlobQuota>(transaction, BlobKeys.Global) is null)
        {
            BlobQuotaOperations.ProveEmpty(transaction, maximumCatalogRecords);
            transaction.PutRecord(BlobKeys.Global, BlobQuota.Empty(destination));
        }
        var quotaKey = BlobKeys.Quota(blob);
        if (BlobRecordReader.Get<BlobQuota>(transaction, quotaKey) is null)
        { transaction.PutRecord(quotaKey, BlobQuota.Empty(destination)); }
        BlobQuotaOperations.Change(transaction, blob, destination, resource.BlobPolicy ?? new BlobPolicy(),
            bytes, keys, versions, uploads);
    }
}
