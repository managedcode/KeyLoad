using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeOperationOwnershipTests
{
    [Test]
    public async Task VerifiedSnapshotSurvivesCallerBufferMutationAndAppliesItsOriginalBody()
    {
        using var database = new TestDatabase();
        database.Configure(NativeAuthorityFixture.Collection, ResourceKind.Collection);
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, database.Partition,
            [new PutDocument(NativeAuthorityFixture.Collection, NativeAuthorityFixture.Entity, NativeAuthorityFixture.Json)]);
        var input = NativeSerialization.Serialize(request);
        var issued = database.Database.CreateCommand(OperationKind.Batch, id, NativeAuthorityFixture.Root,
            database.Database.EvaluationClock.GetUtcNow(), input);
        Array.Clear(input);
        var callerBytes = issued.Value!.Input.ToArray();
        var caller = database.Database.OwnCommand(issued);
        caller.Value = caller.Value! with { Input = callerBytes };
        var verified = database.Database.OwnCommand(caller);
        Array.Clear(callerBytes);
        var first = database.Database.Apply(verified);
        var replay = database.Database.Apply(database.Database.OwnCommand(verified));
        await Assert.That(Errors.Code(first.Problem)).IsNull();
        await Assert.That(first.Get<CommitReceipt>().Token).IsEqualTo(replay.Get<CommitReceipt>().Token);
        var entity = database.Database.GetDocument(NativeAuthorityFixture.Root,
            new(database.Partition, NativeAuthorityFixture.Collection, NativeAuthorityFixture.Entity))!;
        await Assert.That(entity.Json).IsEqualTo(NativeAuthorityFixture.Json);
        await Assert.That(entity.Revision).IsEqualTo(1L);
        await Assert.That(database.Database.ResolveOutcome(verified).Get<CommitReceipt>().Token).IsEqualTo(first.Get<CommitReceipt>().Token);
    }

    [Test]
    public async Task OwnedSnapshotPreservesIndependentMetadataAndBusinessTime()
    {
        using var database = new TestDatabase();
        var operation = NativeAuthorityFixture.Create(database);
        var restamped = database.Database.OwnCommand(operation);
        restamped.Timestamp = restamped.Timestamp.AddTicks(1);
        var verified = database.Database.OwnCommand(restamped);
        await Assert.That(verified.Timestamp).IsEqualTo(restamped.Timestamp);
        await Assert.That(verified.Timestamp).IsNotEqualTo(operation.Timestamp);
        await Assert.That(verified.Value!.Fingerprint).IsEqualTo(operation.Value!.Fingerprint);
        await Assert.That(verified.Value.Input.Span.SequenceEqual(operation.Value.Input.Span)).IsTrue();
    }

    [Test]
    public async Task ConfiguredNativeBudgetRejectsAnOversizeSnapshotBeforeAnyStoreEffect()
    {
        using var database = new TestDatabase();
        var operation = NativeAuthorityFixture.Create(database);
        var engine = new DatabaseEngine(database.Store, database.Database.Authorization, UnitExecutionOptions.DatabaseLimits(database.Database.Limits with
        {
            MaxBatchBytes = operation.Value!.Input.Length - 1
        }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());
        var before = database.Store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => engine.OwnCommand(operation));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(database.Store.Position).IsEqualTo(before);
    }
}
