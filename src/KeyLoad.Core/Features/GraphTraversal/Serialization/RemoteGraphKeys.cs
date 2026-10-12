namespace KeyLoad.Core.Features.GraphTraversal.Serialization;

internal static class RemoteGraphKeys
{
    internal static byte[] Intent(PartitionRef source, string graph, string edgeId, EntityRef target)
        => KeySpace.Partition(RemoteGraphProtocol.IntentFamily, source, graph, edgeId, target.Partition.TenantId,
            target.Partition.DatabaseId, target.Partition.TransactionDomainId, target.Partition.PartitionKey,
            target.Collection, target.Id);

    internal static byte[] IntentPrefix(PartitionRef source, string graph, string edgeId)
        => KeySpace.Partition(RemoteGraphProtocol.IntentFamily, source, graph, edgeId);

    internal static byte[] Receiver(EntityRef target, string graph, PartitionRef source, string edgeId)
        => KeySpace.Partition(RemoteGraphProtocol.ReceiverFamily, target.Partition,
            graph, target.Collection, target.Id, source.TenantId, source.DatabaseId,
            source.TransactionDomainId, source.PartitionKey, edgeId);

    internal static byte[] ReceiverPrefix(EntityRef target, string graph)
        => KeySpace.Partition(RemoteGraphProtocol.ReceiverFamily, target.Partition,
            graph, target.Collection, target.Id);

    internal static byte[] Completion(PartitionRef source, string graph, string edgeId, EntityRef target,
        string intentFingerprint)
        => KeySpace.Partition(RemoteGraphProtocol.CompletionFamily, source, graph, edgeId, target.Partition.TenantId,
            target.Partition.DatabaseId, target.Partition.TransactionDomainId, target.Partition.PartitionKey,
            target.Collection, target.Id, intentFingerprint);

    internal static byte[] CompletionPrefix(PartitionRef source, string graph, string edgeId, EntityRef target)
        => KeySpace.Partition(RemoteGraphProtocol.CompletionFamily, source, graph, edgeId, target.Partition.TenantId,
            target.Partition.DatabaseId, target.Partition.TransactionDomainId, target.Partition.PartitionKey,
            target.Collection, target.Id);

    internal static byte[] Capacity(PartitionRef partition, string direction)
        => KeySpace.Partition(RemoteGraphProtocol.CapacityFamily, partition, direction);
}
