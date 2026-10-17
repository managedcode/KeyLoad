namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Closed configured-owner transport actions; private capture apply is receiver-owned release only.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementProtocol.ActionAlias)]
internal enum PartitionMovementTransportAction
{
    Apply = 1,
    Capture = 2,
    Page = 3,
    Release = 4
}
