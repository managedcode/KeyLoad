namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionControlGrantSnapshot.Alias)]
internal sealed record PartitionControlGrantSnapshot(
    [property: Orleans.Id(0)] PartitionMovePhaseGrant Grant,
    [property: Orleans.Id(1)] PartitionMoveJournalReceipt Authorization)
{
    internal const string Alias = "keyload.partition-control-grant-snapshot.v1";
}
