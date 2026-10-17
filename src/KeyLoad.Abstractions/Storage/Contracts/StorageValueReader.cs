namespace KeyLoad.Storage;

/// <summary>Consumes a stored value while its owning read or transaction gate is held.</summary>
/// <param name="value">Borrowed immutable bytes valid only for this callback.</param>
public delegate void StorageValueReader(ReadOnlySpan<byte> value);
