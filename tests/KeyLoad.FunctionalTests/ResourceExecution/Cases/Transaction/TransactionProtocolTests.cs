namespace KeyLoad.FunctionalTests.ResourceExecution;

internal sealed class TransactionProtocolTests
{
    [Test]
    [Arguments("mutation")]
    [Arguments("patch")]
    [Arguments("event")]
    [Arguments("topic")]
    [Arguments("sample")]
    [Arguments("patch-enum")]
    [Arguments("revision-enum")]
    public async Task MalformedMutationElementsBecomePersistedRejectionsRatherThanApplyExceptions(string shape)
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("events", ResourceKind.StreamSet);
        db.Configure("topics", ResourceKind.Topic);
        db.Configure("samples", ResourceKind.TimeSeries);
        Mutation mutation = shape switch
        {
            "mutation" => null!,
            "patch" => new PatchDocument("orders", "a", [null!], 1),
            "event" => new AppendEvents("events", "a", [null!], ExpectedStreamRevision.NoStream),
            "topic" => new PublishTopic("topics", [null!]),
            "sample" => new AppendSamples("samples", "a", [null!]),
            "patch-enum" => new PatchDocument("orders", "a", [new("/field", (PatchKind)99)], 1),
            _ => new AppendEvents("events", "a", [new("e", "Created", "{}")], new((ExpectedStreamState)99))
        };
        var id = Guid.NewGuid();
        var result = db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition, [mutation]), id: id);
        await Assert.That(Errors.Code(result.Problem).Value).IsEqualTo(ErrorCode.Validation);
        var retained = OutcomeStoreOracle.ReadPartition(db.Store, db.Partition, "root", id)
            ?? throw new InvalidOperationException("The malformed command outcome is missing.");
        await Assert.That(Errors.Code(retained.Problem).Value).IsEqualTo(ErrorCode.Validation);
        await Assert.That(db.Database.GetOutboxStatus("root", db.Partition).Head.Tail).IsEqualTo(0);
        db.Commit(new PutDocument("orders", "valid-after-rejection", "{}", 0));
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "orders", "valid-after-rejection"))).IsNotNull();
    }
}
