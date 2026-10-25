using System.Globalization;
using KeyLoad.Storage;
using KeyLoad.UnitTests.DocumentStorage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.OrleansExecution;

internal static class HostedRetainedOperationReadFlow
{
    private const int First = 1;
    private const int Last = 65;
    private const int ColdOpens = 2;
    private const int FaultAndRepairCommits = 2;
    private const string Root = "root";
    private const string Collection = "retained-commands";
    private const string Document = "retained-reader-";
    private const string DocumentJson = "{}";
    private const string MissingCommand = "The original committed command is not retained.";

    internal static Task ReadAndReopenAsync(CancellationToken token)
        => DocumentPatchAtomicityOwner.RunAsync(async fixture =>
        {
            var original = Seed(fixture).Last();
            var cut = DocumentPatchAtomicityState.Read(fixture);
            await EqualAsync(original, Read(fixture, original, token));
            var changed = Create(fixture, Last + First, original.PreparedCommand.CommandId);
            var refusal = Assert.ThrowsExactly<KeyLoadException>(() => { _ = fixture.Database.ReadStoredCommand(changed, token).Value; });
            await Assert.That(refusal.Code).IsEqualTo(ErrorCode.Conflict);
            await Assert.That(fixture.Database.ReadStoredCommand(Create(fixture, Last + First), token).Value).IsNull();
            await cut.RequireAsync(DocumentPatchAtomicityState.Read(fixture));
            foreach (var _ in Enumerable.Range(First, ColdOpens))
            {
                await fixture.ReopenAsync();
                await cut.RequireAsync(DocumentPatchAtomicityState.Read(fixture));
                await EqualAsync(original, Read(fixture, original, token));
                await cut.RequireAsync(DocumentPatchAtomicityState.Read(fixture));
            }
            await FollowingAsync(fixture, token);
        });

    internal static Task MissingAsync(CancellationToken token)
        => DocumentPatchAtomicityOwner.RunAsync(async fixture =>
        {
            var entries = Seed(fixture);
            var original = entries.Last();
            var key = OutcomeStoreOracle.Key(original.PreparedCommand);
            var bytes = fixture.Store.Read(view => view.ReadOwnedValue(key))
                ?? throw new InvalidOperationException(MissingCommand);
            var clean = DocumentPatchAtomicityState.Read(fixture);
            var changed = fixture.Database.OwnCommand(original.PreparedCommand);
            changed.CommandId = Guid.NewGuid();
            fixture.Store.Commit((transaction, _) =>
            {
                transaction.PutRecord(key, original with { PreparedCommand = changed });
                return true;
            });
            var dirty = DocumentPatchAtomicityState.Read(fixture);
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => Read(fixture, original, token));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
            await dirty.RequireAsync(DocumentPatchAtomicityState.Read(fixture));
            fixture.Store.Commit((transaction, _) =>
            {
                transaction.Put(key, bytes);
                return true;
            });
            await (clean with { Position = clean.Position + FaultAndRepairCommits })
                .RequireAsync(DocumentPatchAtomicityState.Read(fixture));
            await EqualAsync(entries.First(), Read(fixture, entries.First(), token));
            await EqualAsync(original, Read(fixture, original, token));
            await FollowingAsync(fixture, token);
        });

    internal static Task CanceledAsync(CancellationToken token)
        => DocumentPatchAtomicityOwner.RunAsync(async fixture =>
        {
            var original = Seed(fixture).Last();
            var cut = DocumentPatchAtomicityState.Read(fixture);
            using var selected = CancellationTokenSource.CreateLinkedTokenSource(token);
            await selected.CancelAsync();
            var failure = Assert.ThrowsExactly<OperationCanceledException>(() => Read(fixture, original, selected.Token));
            await Assert.That(failure.CancellationToken == selected.Token).IsTrue();
            await Assert.That(token.IsCancellationRequested).IsFalse();
            await cut.RequireAsync(DocumentPatchAtomicityState.Read(fixture));
            await EqualAsync(original, Read(fixture, original, token));
            await cut.RequireAsync(DocumentPatchAtomicityState.Read(fixture));
            await FollowingAsync(fixture, token);
        });

    private static StoredOutcome[] Seed(DocumentPatchAtomicityOwner fixture)
    {
        fixture.Source.Configure(Collection, ResourceKind.Collection);
        return Enumerable.Range(First, Last).Select(index =>
        {
            var command = Create(fixture, index);
            _ = fixture.Submit(command).Get<CommitReceipt>();
            return fixture.Entry(command);
        }).ToArray();
    }

    private static Command<DatabaseCommand> Create(DocumentPatchAtomicityOwner fixture, int index, Guid? commandId = null)
    {
        var id = commandId ?? Guid.NewGuid();
        var payload = new CommandRequest(id, fixture.Source.Partition,
            [new PutDocument(Collection, Document + index.ToString(CultureInfo.InvariantCulture), DocumentJson)]);
        return fixture.Database.CreateCommand(OperationKind.Batch, id, Root,
            fixture.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(payload));
    }

    private static StoredCommand Read(DocumentPatchAtomicityOwner fixture, StoredOutcome original, CancellationToken token)
        => fixture.Database.ReadStoredCommand(original.PreparedCommand, token).Value
            ?? throw new InvalidOperationException(MissingCommand);

    private static async Task EqualAsync(StoredOutcome expected, StoredCommand actual)
    {
        await Assert.That(actual.Sequence).IsEqualTo(expected.CanonicalSequence);
        await DocumentPatchAtomicityAssertions.BytesAsync(OrleansSerialization.Serialize(expected.PreparedCommand),
            OrleansSerialization.Serialize(actual.Command));
    }

    private static async Task FollowingAsync(DocumentPatchAtomicityOwner fixture, CancellationToken token)
    {
        var command = Create(fixture, Last + First);
        _ = fixture.Submit(command).Get<CommitReceipt>();
        var original = fixture.Entry(command);
        var cut = DocumentPatchAtomicityState.Read(fixture);
        await EqualAsync(original, Read(fixture, original, token));
        await cut.RequireAsync(DocumentPatchAtomicityState.Read(fixture));
    }
}
