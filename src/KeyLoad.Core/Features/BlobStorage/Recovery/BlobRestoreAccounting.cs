using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

internal sealed class BlobRestoreAccounting(DatabaseEngine database, BlobRestoreMarker marker)
{
    internal BlobQuota SourceQuota(IKeyValueView view, byte[] key)
    {
        var quota = BlobRecordReader.Get<BlobQuota>(view, key) ?? throw BlobErrors.Corruption();
        BlobQuotaOperations.Validate(quota, marker.SourceIncarnation);
        return quota;
    }

    internal void AbortReservation(IAtomicTransaction tx, BlobState state)
    {
        if (state.Status != BlobUploadStatus.Active)
        { return; }
        var key = BlobKeys.Quota(state.Blob);
        var resource = SourceQuota(tx, key);
        var global = SourceQuota(tx, BlobKeys.Global);
        tx.PutRecord(key, BlobQuotaOperations.Adjust(resource, -state.RemainingReservation, 0, 0, -1));
        tx.PutRecord(BlobKeys.Global, BlobQuotaOperations.Adjust(global, -state.RemainingReservation, 0, 0, -1));
    }

    internal void Add(IAtomicTransaction tx, BlobRef blob, long bytes, int keys, int versions, int uploads)
    {
        _ = Resource(tx, blob);
        var key = BlobRestoreFence.Account(blob);
        var counter = BlobRecordReader.Get<BlobQuota>(tx, key) ?? BlobQuota.Empty(marker.TargetIncarnation);
        BlobRecordReader.Version(counter.FormatVersion);
        if (counter.Incarnation != marker.TargetIncarnation)
        { throw BlobErrors.Corruption(); }
        tx.PutRecord(key, BlobQuotaOperations.Adjust(counter, bytes, keys, versions, uploads));
    }

    internal ResourceDefinition Resource(IKeyValueView view, BlobRef blob)
    {
        var resource = database.Resource(view, blob.Partition, blob.Resource, ResourceKind.BlobStore);
        BlobQuotaOperations.ValidatePolicy(resource);
        return resource;
    }

    internal void VerifyQuota(IAtomicTransaction tx, KeyValueRecord record)
    {
        var blob = BlobRestoreFence.QuotaScope(record.Key.Span, BlobKeys.QuotaSpace);
        var resource = Resource(tx, blob);
        var persisted = BlobRecordReader.Decode<BlobQuota>(record.Value.Span);
        BlobQuotaOperations.Validate(persisted, marker.SourceIncarnation);
        var accountKey = BlobRestoreFence.Account(blob);
        var account = BlobRecordReader.Get<BlobQuota>(tx, accountKey) ?? BlobQuota.Empty(marker.TargetIncarnation);
        BlobQuotaOperations.Validate(account, marker.TargetIncarnation);
        if (!EqualCounters(persisted, account))
        { throw BlobErrors.Corruption(); }
        var policy = resource.BlobPolicy ?? new BlobPolicy();
        if (account.ReservedBytes > policy.MaxReservedBytes || account.ObjectKeys > policy.MaxObjectKeys
            || account.Versions > policy.MaxVersions || account.Uploads > policy.MaxUploads)
        { throw BlobErrors.Corruption(); }
        tx.PutRecord(record.Key.ToArray(), persisted with { Incarnation = marker.TargetIncarnation });
        tx.Delete(accountKey);
    }

    internal static bool EqualCounters(BlobQuota left, BlobQuota right) => left.ReservedBytes == right.ReservedBytes
        && left.ObjectKeys == right.ObjectKeys && left.Versions == right.Versions && left.Uploads == right.Uploads;
}
