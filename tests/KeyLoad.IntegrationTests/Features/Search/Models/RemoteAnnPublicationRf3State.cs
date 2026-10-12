namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record RemoteAnnPublicationRf3State(NativeAnnMaintenanceRf3Scenario Scenario,
    AnnMaintenanceRequest Original, AnnMaintenanceResult Completed, CommandRequest Seed, CommitReceipt SeedReceipt)
{
    internal ApproximateSearchRequest Approximate()
        => new(RemoteAnnPublicationRf3Protocol.Version, Scenario.Search(), Original.Consumer, Original.IndexGeneration);

    internal WaitForAnnIndexRequest Wait()
    {
        var cut = Completed.Source ?? throw new InvalidOperationException(RemoteAnnPublicationRf3Protocol.InvalidOwner);
        return new(Scenario.Partition, Original.Collection, Original.Field, Original.Space, Original.Consumer,
            Original.IndexGeneration, new(cut.Incarnation, Scenario.Partition.AtomicPartitionId,
                cut.AppliedPosition, Original.Placement.PlacementEpoch));
    }
}
