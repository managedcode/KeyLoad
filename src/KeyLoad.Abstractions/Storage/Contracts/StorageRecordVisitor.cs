namespace KeyLoad.Storage;

/// <summary>Consumes one ordered record without allocating an intermediate owned page.</summary>
/// <param name="key">Borrowed immutable key valid only for this callback.</param>
/// <param name="value">Borrowed immutable value valid only for this callback.</param>
/// <returns>True to continue; false to stop before examining another record.</returns>
public delegate bool StorageRecordVisitor(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value);
