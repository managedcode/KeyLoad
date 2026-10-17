using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

/// <summary>Acquires one immutable bounded raw-record page inside the caller-owned restore read cut.</summary>
internal static class BlobRestorePageReader
{
    internal static List<KeyValueRecord> Collect(IKeyValueView view, BlobRestoreMarker marker, BlobExecutionOptions policy)
    {
        const long BytesInitialValue = 0L;
        const int EmptyCollectedRecordCount = 0;

        var records = new List<KeyValueRecord>(policy.MetadataPageSize);
        var prefix = KeyCodec.Encode(BlobRestoreFence.Space(marker.Phase));
        var bytes = BytesInitialValue;
        view.VisitRange(prefix, policy.MetadataPageSize, (key, value) =>
        {
            if (marker.Phase is BlobRestorePhase.States or BlobRestorePhase.Heads
                or BlobRestorePhase.VerifyStates or BlobRestorePhase.VerifyHeads)
            {
                BlobMetadataRules.EncodedLength(value.Length);
            }
            var candidate = checked((long)key.Length + value.Length);
            if (candidate > policy.RestorePageBytes - bytes && records.Count > EmptyCollectedRecordCount)
            {
                return false;
            }
            if (candidate > policy.RestorePageBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, BlobErrors.RestoreWorkBudget);
            }
            records.Add(new(key.ToArray(), value.ToArray()));
            bytes = checked(bytes + candidate);
            return records.Count < policy.MetadataPageSize && bytes < policy.RestorePageBytes;
        }, marker.ExclusiveCursor?.ToArray());
        return records;
    }
}
