using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.RelationalStorage;

internal sealed class NonTransactionalPublicationTests
{
    [Test]
    public async Task FailureAfterPlainWalSyncRetainsDataWithoutOutcomeThenColdOwnerContinues()
    {
        var armed = false;
        var original = new IOException("The original plain WAL boundary failure.");
        using var fixture = new NonTransactionalTableFixture((stage, _, _) =>
        {
            if (armed && stage == CommitStage.PlainWriteAheadLogSynced)
            {
                throw original;
            }
        });
        (await fixture.ConfigureAsync()).Get<ResourceDefinition>();
        var before = fixture.Store.Position;
        var id = Guid.NewGuid();
        armed = true;
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => fixture.BatchAsync(id,
            new PutDocument(RelationalTestData.Table, RelationalTestData.First, RelationalTestData.Row())));
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.UnknownWriteOutcome);
        await Assert.That(failure.InnerException).IsSameReferenceAs(original);
        var refused = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Read(view => view.ReadOwnedValue(fixture.RowKey())));
        await Assert.That(refused.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        fixture.Store.Dispose();
        using var cold = NonTransactionalTableFixture.Open(fixture.Owner.Directory);
        await Assert.That(cold.Position).IsEqualTo(before);
        await Assert.That(cold.Read(view => view.ReadOwnedValue(fixture.OutcomeKey(id)))).IsNull();
        var database = fixture.Owner.CreateConfiguredDatabase(cold);
        var retained = database.GetDocument(RelationalTestData.Root,
            new(fixture.Partition, RelationalTestData.Table, RelationalTestData.First));
        await Assert.That(retained!.Json).IsEqualTo(RelationalTestData.Row());
        await Assert.That(retained.Revision).IsEqualTo(1L);
        var following = Guid.NewGuid();
        (await new EmbeddedCoordinator(database).RuntimeSubmitAsync(OperationKind.Batch, following, RelationalTestData.Root,
            new CommandRequest(following, fixture.Partition, [new PutDocument(RelationalTestData.Table,
                RelationalTestData.Second, NonTransactionalTableFixture.Row(RelationalTestData.Second, NonTransactionalTableFixture.Beta))])))
            .Get<CommitReceipt>();
    }
}
