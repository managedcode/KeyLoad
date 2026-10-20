using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

/// <summary>Checks node-bound publication against its actual persisted local outcome and canonical cut.</summary>
internal static class RequestCqrsAnnCheckpointAssertions
{
    private const long LocalCanonicalSequence = 0;
    private const int EmptyEffects = 0;

    internal static async Task RequireAsync(RequestCqrsHostedRuntimeFixture fixture,
        AnnMaintenanceRequest request, AnnMaintenanceResult actual, long beforeCanonicalPosition, long beforeStorePosition)
    {
        var manifest = RequestCqrsAnnResultAssertions.Manifest(fixture, request);
        var intent = manifest.CheckpointIntent ?? throw new InvalidOperationException();
        var checkpoint = actual.Checkpoint ?? throw new InvalidOperationException();
        var database = fixture.Database;
        var through = request.Mode == AnnMaintenanceMode.Build
            ? RequestCqrsAnnTestData.OriginalTail : RequestCqrsAnnTestData.ChangedTail;
        var position = checkpoint.Receipt.Token.Position;
        var expected = new ProjectionBatchResult(new CommitReceipt(intent.CommandId, new(database.Store.Identity.Incarnation, database.Partition.AtomicPartitionId, position), [], database.Database.Durability, (database.Database.Durability == DurabilityProfile.Buffered ? 0 : 1)), false, through);
        var stored = database.Store.Read(view => view.GetRecord<StoredOutcome>(OutcomeStoreOracle.PartitionKey(
            request.Consumer.Partition, RequestCqrsAnnTestData.Principal, intent.CommandId)))
            ?? throw new InvalidOperationException();

        await RequestCqrsAnnAssertions.EqualAsync(request.Consumer, intent.Consumer);
        await Assert.That(intent.Effects.Length).IsEqualTo(EmptyEffects);
        await RequestCqrsAnnAssertions.EqualAsync(expected, checkpoint);
        await Assert.That(position).IsGreaterThan(beforeStorePosition);
        await Assert.That(position).IsLessThanOrEqualTo(database.Store.Position);
        await Assert.That(stored.CanonicalSequence).IsEqualTo(LocalCanonicalSequence);
        await RequestCqrsAnnAssertions.EqualAsync(checkpoint, stored.Result.Get<ProjectionBatchResult>());
        await Assert.That(database.ReadCanonicalCut().AppliedPosition).IsEqualTo(beforeCanonicalPosition);
    }
}
