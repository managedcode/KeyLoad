namespace KeyLoad.Core.Features.GraphTraversal.Serialization;

/// <summary>Builds a fixed native tuple tree independent of the caller's CLR reference sharing.</summary>
internal static class GraphCrossPartitionFingerprintInput
{
    internal static GraphCrossPartitionFingerprintV1 Create(PartitionRef source, string graph,
        string edgeId, EntityRef destination, long revision, bool deleted, EdgeRecord edge)
        => new(GraphCrossPartitionProtocol.CurrentVersion, Copy(source), Copy(graph), Copy(edgeId),
            Copy(destination), revision, deleted,
            new EdgeRecord(Copy(edge.Id), Copy(edge.From), Copy(edge.To), Copy(edge.Label),
                Copy(edge.AttributesJson), edge.Revision));

    private static PartitionRef Copy(PartitionRef partition)
        => new(Copy(partition.TenantId), Copy(partition.DatabaseId),
            Copy(partition.TransactionDomainId), Copy(partition.PartitionKey));

    private static EntityRef Copy(EntityRef entity)
        => new(Copy(entity.Partition), Copy(entity.Collection), Copy(entity.Id));

    private static string Copy(string value) => new(value.AsSpan());
}
