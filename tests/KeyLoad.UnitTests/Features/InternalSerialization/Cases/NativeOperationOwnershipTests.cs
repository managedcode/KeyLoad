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
        var issued = database.Database.CreateNativeOperation(OperationKind.Batch, id, NativeAuthorityFixture.Root,
            database.Database.EvaluationClock.GetUtcNow(), input);
        Array.Clear(input);
        var callerBytes = issued.NativePayload.ToArray();
        var caller = issued with { NativePayload = callerBytes };
        var verified = database.Database.VerifyOperationAuthority(caller);
        Array.Clear(callerBytes);
        var first = database.Database.Apply(verified);
        var replay = database.Database.Apply(verified with { NativePayload = verified.NativePayload.ToArray() });
        await Assert.That(first.Error).IsNull();
        await Assert.That(first.Get<CommitReceipt>().Token).IsEqualTo(replay.Get<CommitReceipt>().Token);
        var entity = database.Database.GetDocument(NativeAuthorityFixture.Root,
            new(database.Partition, NativeAuthorityFixture.Collection, NativeAuthorityFixture.Entity))!;
        await Assert.That(entity.Json).IsEqualTo(NativeAuthorityFixture.Json);
        await Assert.That(entity.Revision).IsEqualTo(1L);
        await Assert.That(database.Database.ResolveOutcome(verified).Get<CommitReceipt>().Token).IsEqualTo(first.Get<CommitReceipt>().Token);
    }

    [Test]
    public async Task BusinessTimeCanBeStampedAfterIssuanceButRemainsPartOfRetryEquality()
    {
        using var database = new TestDatabase();
        var operation = NativeAuthorityFixture.Create(database);
        var restamped = operation with { EvaluatedAt = operation.EvaluatedAt.AddTicks(1) };
        var verified = database.Database.VerifyOperationAuthority(restamped);
        await Assert.That(verified.EvaluatedAt).IsEqualTo(restamped.EvaluatedAt);
        await Assert.That(database.Database.NativeOperationsEqual(operation, restamped)).IsFalse();
        await Assert.That(database.Database.NativeOperationsEqual(operation,
            operation with { NativePayload = operation.NativePayload.ToArray() })).IsTrue();
    }

    [Test]
    public async Task ConfiguredNativeBudgetRejectsAnOversizeSnapshotBeforeAnyStoreEffect()
    {
        using var database = new TestDatabase();
        var operation = NativeAuthorityFixture.Create(database);
        var engine = new DatabaseEngine(database.Store, database.Database.Authorization,
            database.Database.Limits with { MaxBatchBytes = operation.NativePayload.Length - 1 });
        var before = database.Store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => engine.VerifyOperationAuthority(operation));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(database.Store.Position).IsEqualTo(before);
    }
}
