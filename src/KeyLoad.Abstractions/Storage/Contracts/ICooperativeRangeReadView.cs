namespace KeyLoad.Storage;

/// <summary>Visits one forward range while allowing its owner to yield between borrowed callbacks.</summary>
internal interface ICooperativeRangeReadView : IKeyValueView
{
    ValueTask<StorageScanResult> VisitRangeAsync(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey, CancellationToken cancellationToken);

    /// <summary>Opens ordered point probes for an admitted transactional prefix in the current read callback.</summary>
    IOrderedPointReader OpenOrderedPointReader(byte[] prefix, CancellationToken cancellationToken);
}
