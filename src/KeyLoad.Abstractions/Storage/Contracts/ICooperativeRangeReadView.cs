namespace KeyLoad.Storage;

/// <summary>Visits one forward range while allowing its owner to yield between borrowed callbacks.</summary>
internal interface ICooperativeRangeReadView : IKeyValueView
{
    ValueTask<StorageScanResult> VisitRangeAsync(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey, CancellationToken cancellationToken);
}
