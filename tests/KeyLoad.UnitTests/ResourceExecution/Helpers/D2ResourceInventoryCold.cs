using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ResourceInventoryCold
{
    private const string PartitionKey = "p3";
    private const string DocumentId = "additional";
    private const string Json = "{\"value\":3}";

    internal static async Task ExecuteAsync(D2ResourceInventoryFixture fixture, D2ResourceInventorySeed seed,
        CancellationToken token)
    {
        var originalWork = fixture.Work(token);
        var control = fixture.Capture(seed.A, originalWork);
        var page = fixture.Page(control, seed.Observed, null, originalWork);
        var cursor = page.Continuation ?? throw new InvalidOperationException("The original D2 cursor is required.");
        var partition = new PartitionRef(D2ResourceInventoryData.Tenant, D2ResourceInventoryData.Database,
            D2ResourceInventoryData.Domain, PartitionKey);
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, partition,
            [new PutDocument(D2ResourceInventoryData.A, DocumentId, Json, D2ResourceInventoryData.EmptyRevision)]);
        var original = fixture.Database.CreateNativeOperation(OperationKind.Batch, id, D2ResourceInventoryData.Root,
            fixture.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(command));
        var result = fixture.Submit(original, token);
        await Assert.That(result.Get<CommitReceipt>().CommandId).IsEqualTo(id);
        await Assert.That(result.Get<CommitReceipt>().Token.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        var before = ControlledPartitionMovementExpiryOwnerState.Capture(fixture.Owner);
        await D2ResourceInventoryRefusals.FailureAsync(() =>
            fixture.Page(control, seed.Observed, cursor, fixture.Work(token)), ErrorCode.RevisionConflict);
        await D2ResourceInventoryRefusals.FailureAsync(() =>
            fixture.Database.ValidateResourceOwnerInventoryOwner(D2ResourceInventoryData.Administrator,
                control, page.Cut, fixture.Work(token)), ErrorCode.RevisionConflict);
        await before.AssertUnchangedAsync(fixture.Owner);
        PartitionRef[] expected = [.. D2ResourceInventoryData.Partitions, partition];
        for (var pass = 0; pass < D2ResourceInventoryData.TwoColdOwners; pass++)
        {
            await D2ResourceInventoryAssertions.RowsAsync(fixture, seed, seed.A, token, expected);
            await D2ResourceInventoryAssertions.RowsAsync(fixture, seed, seed.B, token, expected);
            await D2ResourceInventoryAssertions.EmptyAsync(fixture, seed, token);
            await D2ResourceInventoryAssertions.EqualAsync(result, fixture.Submit(original, token));
            await D2ResourceInventoryCold.ReplayAsync(fixture, seed, token);
            await NewDocumentAsync(fixture, partition, token);
            await before.AssertUnchangedAsync(fixture.Owner);
            fixture.Owner.Reopen();
            await before.AssertUnchangedAsync(fixture.Owner);
        }
        await D2ResourceInventoryAssertions.RowsAsync(fixture, seed, seed.A, token, expected);
        await D2ResourceInventoryAssertions.RowsAsync(fixture, seed, seed.B, token, expected);
        await D2ResourceInventoryAssertions.EqualAsync(result, fixture.Submit(original, token));
        await D2ResourceInventoryAssertions.EmptyAsync(fixture, seed, token);
        await ReplayAsync(fixture, seed, token);
        await NewDocumentAsync(fixture, partition, token);
        await before.AssertUnchangedAsync(fixture.Owner);
    }

    private static async Task NewDocumentAsync(D2ResourceInventoryFixture fixture, PartitionRef partition,
        CancellationToken token)
    {
        var document = fixture.Database.GetDocument(D2ResourceInventoryData.Root,
            new(partition, D2ResourceInventoryData.A, DocumentId), cancellationToken: token);
        await Assert.That(document).IsNotNull();
        await Assert.That(document!.Json).IsEqualTo(Json);
        await Assert.That(document.Revision).IsEqualTo(D2ResourceInventoryData.InitialRevision);
    }

    internal static async Task ReplayAsync(D2ResourceInventoryFixture fixture, D2ResourceInventorySeed seed,
        CancellationToken token)
    {
        var before = ControlledPartitionMovementExpiryOwnerState.Capture(fixture.Owner);
        for (var index = 0; index < seed.Writes.Length; index++)
        {
            var repeated = fixture.Submit(seed.Writes[index], token);
            await D2ResourceInventoryAssertions.EqualAsync(seed.Results[index], repeated);
            await D2ResourceInventoryAssertions.EqualAsync(seed.Results[index].Get<CommitReceipt>(), repeated.Get<CommitReceipt>());
            var document = fixture.Database.GetDocument(D2ResourceInventoryData.Root,
                new(D2ResourceInventoryData.Partitions[index], D2ResourceInventoryData.Collections[index],
                    D2ResourceInventoryData.DocumentId), cancellationToken: token);
            await Assert.That(document).IsNotNull();
            await Assert.That(document!.Json).IsEqualTo(D2ResourceInventoryData.Json[index]);
            await Assert.That(document.Revision).IsEqualTo(D2ResourceInventoryData.InitialRevision);
            var tokenValue = seed.Results[index].Get<CommitReceipt>().Token;
            await Assert.That(tokenValue.AtomicPartitionId).IsEqualTo(D2ResourceInventoryData.Partitions[index].AtomicPartitionId);
        }
        await before.AssertUnchangedAsync(fixture.Owner);
    }
}
