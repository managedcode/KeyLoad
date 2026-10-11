namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Checks the complete acknowledgement against the independently observed pre-operation journal.</summary>
internal static class RequestCqrsAnnCheckpointAssertions
{
    private const long MaintenanceCommitCount = 2;
    private const int EmptyEffects = 0;

    internal static async Task RequireAsync(RequestCqrsClusterFixture fixture,
        AnnMaintenanceRequest request, AnnMaintenanceResult actual, long beforeJournalIndex)
    {
        // This bounded corpus has one replay page: configuration and checkpoint each own one ordered commit.
        var manifest = RequestCqrsAnnResultAssertions.Manifest(fixture, request);
        var intent = manifest.CheckpointIntent ?? throw new InvalidOperationException();
        var checkpoint = actual.Checkpoint ?? throw new InvalidOperationException();
        var database = fixture.Database;
        var placement = database.Database.ReadAtomicPartitionPlacement(RequestCqrsAnnTestData.Principal,
            new(RequestCqrsAnnTestData.Version, database.Partition));
        var through = request.Mode == AnnMaintenanceMode.Build
            ? RequestCqrsAnnTestData.OriginalTail : RequestCqrsAnnTestData.ChangedTail;
        var expectedPosition = checked(beforeJournalIndex + MaintenanceCommitCount);
        var expected = new ProjectionBatchResult(new CommitReceipt(intent.CommandId,
            new(database.Store.Identity.Incarnation, database.Partition.AtomicPartitionId,
                expectedPosition, placement.PlacementEpoch), [], database.Store.Identity.Durability),
            false, through);

        await RequestCqrsAnnAssertions.EqualAsync(request.Consumer, intent.Consumer);
        await Assert.That(intent.Effects.Length).IsEqualTo(EmptyEffects);
        await RequestCqrsAnnAssertions.EqualAsync(expected, checkpoint);
        await Assert.That(database.ReadJournalCut().CommittedIndex).IsEqualTo(expectedPosition);
        await Assert.That(database.ReadJournalCut().LastIndex).IsEqualTo(expectedPosition);
    }
}
