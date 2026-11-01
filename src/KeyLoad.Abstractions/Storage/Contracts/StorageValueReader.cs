namespace KeyLoad.Storage;

/// <summary>Consumes a stored value within its owning read or transaction callback.</summary>
/// <param name="value">Borrowed immutable bytes valid only for this callback.</param>
public delegate void StorageValueReader(ReadOnlySpan<byte> value);
