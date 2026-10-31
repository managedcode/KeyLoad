namespace KeyLoad.FunctionalTests.InternalSerialization;

internal sealed class OperationOwnershipTests
{
    [Test]
    public async Task VerifiedSnapshotSurvivesCallerBufferMutationAndAppliesItsOriginalBody()
    {
        using var database = new TestDatabase();
        database.Configure(AuthorityFixture.Collection, ResourceKind.Collection);
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, database.Partition,
            [new PutDocument(AuthorityFixture.Collection, AuthorityFixture.Entity, AuthorityFixture.Json)]);
        var issued = database.Database.CreateCommand(OperationKind.Batch, id, AuthorityFixture.Root,
            database.Database.EvaluationClock.GetUtcNow(), request);
        var caller = database.Database.OwnCommand(issued);
        caller.Metadata = new()
        {
            Tags = new() { [AuthorityFixture.Entity] = AuthorityFixture.Json },
            Properties = new() { [AuthorityFixture.Entity] = new byte[] { 1, 2, 3 } }
        };
        var original = OrleansSerialization.Serialize(caller);
        var verified = database.Database.OwnCommand(caller);
        caller.Metadata.Tags[AuthorityFixture.Entity] = AuthorityFixture.InvalidJson;
        Array.Clear((byte[])caller.Metadata.Properties[AuthorityFixture.Entity]!);
        await Assert.That(OrleansSerialization.Serialize(verified).AsSpan().SequenceEqual(original)).IsTrue();
        var first = database.Database.Apply(verified);
        var replay = database.Database.Apply(database.Database.OwnCommand(verified));
        await Assert.That(Errors.Code(first.Problem).Value).IsNull();
        await Assert.That(first.Get<CommitReceipt>().Token).IsEqualTo(replay.Get<CommitReceipt>().Token);
        var entity = database.Database.GetDocument(AuthorityFixture.Root,
            new(database.Partition, AuthorityFixture.Collection, AuthorityFixture.Entity))!;
        await Assert.That(entity.Json).IsEqualTo(AuthorityFixture.Json);
        await Assert.That(entity.Revision).IsEqualTo(1L);
        await Assert.That(database.Database.ResolveOutcome(verified).Get<CommitReceipt>().Token).IsEqualTo(first.Get<CommitReceipt>().Token);
    }

    [Test]
    public async Task OwnedSnapshotPreservesIndependentMetadataAndBusinessTime()
    {
        using var database = new TestDatabase();
        var operation = AuthorityFixture.Create(database);
        var restamped = database.Database.OwnCommand(operation);
        restamped.Timestamp = restamped.Timestamp.AddTicks(1);
        var verified = database.Database.OwnCommand(restamped);
        await Assert.That(verified.Timestamp).IsEqualTo(restamped.Timestamp);
        await Assert.That(verified.Timestamp).IsNotEqualTo(operation.Timestamp);
        await Assert.That(verified.Value!.Fingerprint).IsEqualTo(operation.Value!.Fingerprint);
        await Assert.That(OrleansSerialization.Serialize(verified.Value.Input, ValidationProfile.PublicInputElements).AsSpan().SequenceEqual(OrleansSerialization.Serialize(operation.Value.Input, ValidationProfile.PublicInputElements))).IsTrue();
    }

}
