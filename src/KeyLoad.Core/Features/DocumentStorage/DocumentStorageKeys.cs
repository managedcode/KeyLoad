namespace KeyLoad.Core.Features.DocumentStorage;

/// <summary>Constructs the canonical persisted document key space.</summary>
public static class DocumentStorageKeys
{
    private const string DocumentSpace = "document";

    /// <summary>Constructs a document key from its qualified identity.</summary>
    /// <param name="reference">Qualified document identity.</param>
    /// <returns>The existing ordered document key bytes.</returns>
    public static byte[] RecordKey(EntityRef reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return RecordKey(reference.Partition, reference.Collection, reference.Id);
    }

    /// <summary>Constructs a document key within an atomic partition.</summary>
    /// <param name="partition">Owning atomic partition.</param>
    /// <param name="collection">Configured collection.</param>
    /// <param name="id">Document identifier.</param>
    /// <returns>The existing ordered document key bytes.</returns>
    public static byte[] RecordKey(PartitionRef partition, string collection, string id)
    {
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(id);
        return KeySpace.Partition(DocumentSpace, partition, collection, id);
    }

    /// <summary>Constructs the ordered prefix for one collection in a partition.</summary>
    /// <param name="partition">Owning atomic partition.</param>
    /// <param name="collection">Configured collection.</param>
    /// <returns>The existing collection prefix bytes.</returns>
    public static byte[] Prefix(PartitionRef partition, string collection)
    {
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(collection);
        return KeySpace.Partition(DocumentSpace, partition, collection);
    }
}
