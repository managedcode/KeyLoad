namespace KeyLoad.Storage;

/// <summary>Consumes a stored value while its owning read or transaction gate is held.</summary>
/// <param name="value">Borrowed immutable bytes valid only for this callback.</param>
public delegate void StorageValueReader(ReadOnlySpan<byte> value);

/// <summary>Consumes one ordered record without allocating an intermediate owned page.</summary>
/// <param name="key">Borrowed immutable key valid only for this callback.</param>
/// <param name="value">Borrowed immutable value valid only for this callback.</param>
/// <returns>True to continue; false to stop before examining another record.</returns>
public delegate bool StorageRecordVisitor(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value);

/// <summary>Accounts examined work before the consumer can copy or decode record bytes.</summary>
/// <param name="byteCount">Examined key and value bytes, including work hidden by staged changes.</param>
public delegate void StorageReadObserver(long byteCount);

/// <summary>Describes a completed gate-scoped range visit without retaining record buffers.</summary>
/// <param name="Records">Number of logical records delivered to the visitor.</param>
/// <param name="HasMore">Whether a record-limit lookahead found another logical record.</param>
/// <param name="StoppedByVisitor">Whether the visitor explicitly stopped the traversal.</param>
/// <param name="ReadBytes">Examined raw key/value bytes, including charged lookahead work.</param>
public readonly record struct StorageScanResult(int Records, bool HasMore, bool StoppedByVisitor, long ReadBytes);
