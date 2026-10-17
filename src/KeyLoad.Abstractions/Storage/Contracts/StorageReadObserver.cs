namespace KeyLoad.Storage;

/// <summary>Accounts examined work before the consumer can copy or decode record bytes.</summary>
/// <param name="byteCount">Examined key and value bytes, including work hidden by staged changes.</param>
public delegate void StorageReadObserver(long byteCount);
