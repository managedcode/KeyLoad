namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class TestDatabaseConcurrentCloseSeeding
{
    internal static async Task<TestDatabaseConcurrentCloseSeed> RunAsync(TestDatabase fixture, Guid id)
    {
        fixture.Configure(TestDatabaseConcurrentCloseProtocol.Collection, ResourceKind.Collection);
        var before = fixture.ReadCanonicalCut();
        var command = new CommandRequest(id, fixture.Partition,
            [new PutDocument(TestDatabaseConcurrentCloseProtocol.Collection, TestDatabaseConcurrentCloseProtocol.Document,
                TestDatabaseConcurrentCloseProtocol.Body, TestDatabaseConcurrentCloseProtocol.MissingRevision)]);
        var result = fixture.Submit(OperationKind.Batch, command, id: id);
        var expectedIndex = checked(before.AppliedPosition + TestDatabaseConcurrentCloseProtocol.IndexStep);
        var expected = new CommitReceipt(id, new(fixture.Store.Identity.Incarnation, fixture.Partition.AtomicPartitionId, expectedIndex), [new(TestDatabaseConcurrentCloseProtocol.MutationKind, TestDatabaseConcurrentCloseProtocol.Collection,
                TestDatabaseConcurrentCloseProtocol.Document, TestDatabaseConcurrentCloseProtocol.Revision)], DurabilityProfile.ProcessDurable, 1);
        await TestDatabaseConcurrentCloseAssertions.ReceiptAsync(result, expected);
        var cut = fixture.ReadCanonicalCut();
        await Assert.That(cut.AppliedPosition).IsEqualTo(expectedIndex);
        await Assert.That(cut.StorePosition).IsEqualTo(checked(before.StorePosition + TestDatabaseConcurrentCloseProtocol.IndexStep));
        var original = fixture.ReadStoredOutcome(id).PreparedCommand ?? throw new InvalidOperationException();
        await Assert.That(original.CommandId).IsEqualTo(id);
        await Assert.That(original.Value!.Kind).IsEqualTo(OperationKind.Batch);
        await Assert.That(original.UserId!).IsEqualTo(TestDatabaseConcurrentCloseProtocol.Principal);
        var state = new TestDatabaseConcurrentCloseSeed(fixture.Partition, original, result, expected,
            fixture.Store.Identity, fixture.Store.Position, TenantAdmissionAssertions.State(fixture.Store),
            fixture.ExpectedReference(new(fixture.Partition, TestDatabaseConcurrentCloseProtocol.Collection,
                TestDatabaseConcurrentCloseProtocol.Document)));
        await TestDatabaseConcurrentCloseAssertions.CurrentAsync(fixture.Database, fixture.Store, state);
        var replay = fixture.SubmitIssued(original);
        await TestDatabaseConcurrentCloseAssertions.ReceiptAsync(replay, expected);
        await ReplayResultAssertions.Same<CommitReceipt>(replay, result);
        await TestDatabaseConcurrentCloseAssertions.CurrentAsync(fixture.Database, fixture.Store, state);
        return state;
    }
}
