using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class InitialPrincipalEpochHealthy
{
    internal static async Task<InitialPrincipalEpochState> CreateAsync(InitialPrincipalEpochFixture fixture, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, fixture.Harness.Partition,
            [new PutDocument(InitialPrincipalEpochFixture.Collection, "record", "{\"value\":7}", 1L)]);
        var operation = InitialPrincipalEpochFixture.Operation(OperationKind.Batch, command, InitialPrincipalEpochFixture.Reader, id);
        var before = fixture.Harness.Store.Position;
        var actual = fixture.Harness.Database.ApplyEmbedded(operation, token).Get<CommitReceipt>();
        var expected = new CommitReceipt(id, new(fixture.Harness.Store.Identity.Incarnation,
            fixture.Harness.Partition.AtomicPartitionId, before + 1L), [new("putDocument", InitialPrincipalEpochFixture.Collection, "record", 2L)], DurabilityProfile.ProcessDurable, 1);
        await InitialPrincipalEpochAssertions.EqualAsync(actual, expected);
        await Assert.That(actual.Mutations.Single().CompositionReferences).IsEmpty();
        await Assert.That(fixture.Harness.Store.Position).IsEqualTo(before + 1L);
        var outcome = fixture.Harness.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(command.Partition,
            InitialPrincipalEpochFixture.Reader, id)))!;
        return new(operation, expected, outcome);
    }
    internal static Task RequireAsync(InitialPrincipalEpochFixture fixture, DatabaseEngine database, ZoneTreeStore store,
        InitialPrincipalEpochState state, Command<DatabaseCommand> refused, CancellationToken token)
        => InitialPrincipalEpochAssertions.ObserveAsync(store, async () =>
        {
            await InitialPrincipalEpochAssertions.CatalogAsync(fixture, store);
            var seedReplay = database.ApplyEmbedded(fixture.Seed, token).Get<CommitReceipt>();
            await InitialPrincipalEpochAssertions.EqualAsync(seedReplay, fixture.SeedReceipt);
            await Assert.That(seedReplay.Mutations.Single().CompositionReferences).IsEmpty();
            await Assert.That(store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(fixture.Harness.Partition,
                "root", fixture.Seed.Id)))!.AsSpan().SequenceEqual(fixture.SeedOutcome)).IsTrue();
            await InitialPrincipalEpochAssertions.RefuseAsync(() => database.Authenticate(InitialPrincipalEpochFixture.OriginalSecret,
                database.EvaluationClock.GetUtcNow()), ErrorCode.Unauthenticated);
            await Assert.That(database.Authenticate(InitialPrincipalEpochFixture.ReplacementSecret, database.EvaluationClock.GetUtcNow()))
                .IsEqualTo(InitialPrincipalEpochFixture.Reader);
            var document = database.GetDocument(InitialPrincipalEpochFixture.Reader, fixture.Reference)!;
            await InitialPrincipalEpochAssertions.EqualAsync(document, new DocumentResult(fixture.Reference, 2L, "{\"value\":7}", false, []));
            var replay = database.ApplyEmbedded(state.Operation, token).Get<CommitReceipt>();
            await InitialPrincipalEpochAssertions.EqualAsync(replay, state.Receipt);
            await Assert.That(replay.Mutations.Single().CompositionReferences).IsEmpty();
            await Assert.That(store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(fixture.Harness.Partition,
                InitialPrincipalEpochFixture.Reader, state.Operation.CommandId)))!.AsSpan().SequenceEqual(state.Outcome)).IsTrue();
            await InitialPrincipalEpochAssertions.FailureAsync(database.ApplyEmbedded(refused, token),
                Result<object>.Fail(Errors.Problem(ErrorCode.Validation, InitialPrincipalEpochFixture.InvalidDetail)));
            await InitialPrincipalEpochAssertions.CatalogAsync(fixture, store);
        });
}
