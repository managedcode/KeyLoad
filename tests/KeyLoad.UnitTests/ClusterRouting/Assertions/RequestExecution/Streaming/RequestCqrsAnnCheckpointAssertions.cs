namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Checks the complete acknowledgement against the independently observed pre-operation journal.</summary>
internal static class RequestCqrsAnnCheckpointAssertions
{
    private const long CheckpointCommitCount = 1;
    private const long MaintenanceCommitCount = 2;
    private const int EmptyEffects = 0;

    internal static async Task RequireAsync(RequestCqrsHostedRuntimeFixture fixture,
        AnnMaintenanceRequest request, AnnMaintenanceResult actual, long beforeJournalIndex)
    {
        // The initial Build configuration was already reserved before the caller captured its journal cut.
        // Build adds one checkpoint then one settlement; Restore adds configuration then checkpoint.
        var manifest = RequestCqrsAnnResultAssertions.Manifest(fixture, request);
        var intent = manifest.CheckpointIntent ?? throw new InvalidOperationException();
        var checkpoint = actual.Checkpoint ?? throw new InvalidOperationException();
        var database = fixture.Database;
        var placement = database.Database.ReadAtomicPartitionPlacement(RequestCqrsAnnTestData.Principal,
            database.Partition);
        var through = request.Mode == AnnMaintenanceMode.Build
            ? RequestCqrsAnnTestData.OriginalTail : RequestCqrsAnnTestData.ChangedTail;
        var checkpointCommits = request.Mode == AnnMaintenanceMode.Build
            ? CheckpointCommitCount : MaintenanceCommitCount;
        var expectedPosition = checked(beforeJournalIndex + checkpointCommits);
        var finalPosition = checked(beforeJournalIndex + MaintenanceCommitCount);
        var expected = new ProjectionBatchResult(new CommitReceipt(intent.CommandId,
            new(database.Store.Identity.Incarnation, database.Partition.AtomicPartitionId,
                expectedPosition, placement.PlacementEpoch), [], database.Store.Identity.Durability),
            false, through);

        await RequestCqrsAnnAssertions.EqualAsync(request.Consumer, intent.Consumer);
        await Assert.That(intent.Effects.Length).IsEqualTo(EmptyEffects);
        await RequestCqrsAnnAssertions.EqualAsync(expected, checkpoint);
        await Assert.That(database.ReadCanonicalCut().AppliedPosition).IsEqualTo(finalPosition);
        await Assert.That(database.ReadCanonicalCut().AppliedPosition).IsEqualTo(finalPosition);
    }
}
