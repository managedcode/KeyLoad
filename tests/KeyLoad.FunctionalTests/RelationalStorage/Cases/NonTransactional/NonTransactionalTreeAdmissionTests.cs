namespace KeyLoad.FunctionalTests.RelationalStorage;

internal sealed class NonTransactionalTreeAdmissionTests
{
    [Test]
    [Arguments("tree")]
    [Arguments("plain-tree")]
    public async Task MissingNativeTreeRefusesReplacementThenOriginalFilesAllowColdHealthyWork(string name)
    {
        using var fixture = new NonTransactionalTableFixture();
        (await fixture.ConfigureAsync()).Get<ResourceDefinition>();
        (await fixture.BatchAsync(Guid.NewGuid(), new PutDocument(RelationalTestData.Table,
            RelationalTestData.First, RelationalTestData.Row()))).Get<CommitReceipt>();
        var position = fixture.Store.Position;
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        var original = fixture.Destination(name);
        var retained = fixture.Destination(name + "-retained");
        Directory.Move(original, retained);
        Assert.ThrowsExactly<DirectoryNotFoundException>(() =>
        {
            using var unexpected = NonTransactionalTableFixture.Open(fixture.Owner.Directory);
        });
        await Assert.That(Directory.Exists(original)).IsFalse();
        Directory.Move(retained, original);
        using var cold = NonTransactionalTableFixture.Open(fixture.Owner.Directory);
        position = await NativeStoreAssertions.ReopenedAsync(cold, position);
        await Assert.That(cold.Position).IsEqualTo(position);
        await Assert.That(OrleansSerialization.Serialize(cold.Identity).SequenceEqual(identity)).IsTrue();
        var database = fixture.Owner.CreateConfiguredDatabase(cold);
        await Assert.That(database.GetDocument(RelationalTestData.Root,
            new(fixture.Partition, RelationalTestData.Table, RelationalTestData.First))!.Json).IsEqualTo(RelationalTestData.Row());
        var following = Guid.NewGuid();
        (await new EmbeddedCoordinator(database).RuntimeSubmitAsync<object>(OperationKind.Batch, following, RelationalTestData.Root,
            new CommandRequest(following, fixture.Partition, [new PutDocument(RelationalTestData.Table,
                RelationalTestData.Second, NonTransactionalTableFixture.Row(RelationalTestData.Second, NonTransactionalTableFixture.Beta))])))
            .Get<CommitReceipt>();
    }
}
