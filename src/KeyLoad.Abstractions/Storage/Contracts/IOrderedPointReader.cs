namespace KeyLoad.Storage;

/// <summary>Borrowed point reads for strictly increasing encoded suffixes within one native read callback.</summary>
internal interface IOrderedPointReader : IDisposable
{
    /// <summary>Reads the record at the exact key and borrows its bytes for the callback only.</summary>
    bool Read(ReadOnlySpan<byte> suffix, StorageValueReader reader);
}
