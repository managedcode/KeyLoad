namespace KeyLoad.Core.Features.Search;

internal static class OnlineTextPublicationKeys
{
    internal static byte[] Prefix(PartitionRef partition, string collection, string field)
        => KeySpace.Partition(OnlineTextPublicationProtocol.CurrentFamily, partition, collection, field);

    internal static byte[] Current(OnlineTextIndexMaintenanceRequest request)
        => KeySpace.Partition(OnlineTextPublicationProtocol.CurrentFamily, request.Consumer.Partition,
            request.Collection, request.Field, request.Consumer.Name, request.ConsumerGeneration);
}
