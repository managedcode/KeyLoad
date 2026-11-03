using KeyLoad.Storage;

namespace KeyLoad.Core.DocumentStorage;

/// <summary>Constructs the canonical persisted document key space.</summary>
public static class DocumentStorageKeys
{
    private const string DocumentSpace = "document";

    /// <summary>Constructs a document key from its qualified identity.</summary>
    /// <param name="reference">Qualified document identity.</param>
    /// <returns>The existing ordered document key bytes.</returns>
    public static byte[] RecordKey(EntityRef reference)
    {
        ArgumentNullException.ThrowIfNull(reference) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        return RecordKey(reference.Partition, reference.Collection, reference.Id);
    }

    /// <summary>Constructs a document key within an atomic partition.</summary>
    /// <param name="partition">Owning atomic partition.</param>
    /// <param name="collection">Configured collection.</param>
    /// <param name="id">Document identifier.</param>
    /// <returns>The existing ordered document key bytes.</returns>
    public static byte[] RecordKey(PartitionRef partition, string collection, string id)
    {
        ArgumentNullException.ThrowIfNull(partition) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(collection) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(id) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        return KeySpace.Partition(DocumentSpace, partition, collection, id);
    }

    internal static byte[] RecordKey(ReadOnlySpan<byte> collectionPrefix, string id)
    {
        ArgumentNullException.ThrowIfNull(id) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        return KeyCodec.AppendComponent(collectionPrefix, id);
    }

    /// <summary>Constructs the ordered prefix for one collection in a partition.</summary>
    /// <param name="partition">Owning atomic partition.</param>
    /// <param name="collection">Configured collection.</param>
    /// <returns>The existing collection prefix bytes.</returns>
    public static byte[] Prefix(PartitionRef partition, string collection)
    {
        ArgumentNullException.ThrowIfNull(partition) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(collection) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        return KeySpace.Partition(DocumentSpace, partition, collection);
    }
}
