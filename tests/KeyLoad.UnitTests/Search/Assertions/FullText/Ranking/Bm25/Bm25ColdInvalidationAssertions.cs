using KeyLoad.Core.Features.Search;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Search;

internal static class Bm25ColdInvalidationAssertions
{
    internal static byte[] HeadBytes(Bm25OriginalPhaseRecoveryOwner owner, Bm25CorpusCurrent head)
    {
        var work = owner.Source.CreateReadWork(Bm25OriginalPhaseRecoveryFlow.Token);
        return owner.Store.Read(view =>
        {
            byte[]? raw = null;
            if (!view.ReadValue(Bm25CorpusKeys.Current(head), bytes => raw = bytes.ToArray(), work.ChargeBytes))
            {
                throw new InvalidOperationException("The original execution BM25 head is missing.");
            }
            return raw ?? throw new InvalidOperationException("The original execution BM25 head was not read.");
        });
    }

    internal static async Task MutationReplayAsync(Bm25OriginalPhaseRecoveryFlow flow,
        (Command<DatabaseCommand> Operation, Result<object> Result, byte[] Entry)[] originals)
    {
        var before = Bm25OriginalPhaseRecoveryState.Read(flow.Owner);
        foreach (var original in originals)
        {
            await ReplayResultAssertions.BytesAsync(original.Entry,
                OrleansSerialization.Serialize(flow.Owner.Entry(original.Operation.CommandId)));
            await ReplayResultAssertions.SameAsync(original.Result, flow.Owner.Submit(original.Operation));
        }
        await before.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(flow.Owner));
    }

    internal static async Task ColdAsync(Bm25OriginalPhaseRecoveryFlow flow, Command<DatabaseCommand> original, byte[] entry)
    {
        var before = Bm25OriginalPhaseRecoveryState.Read(flow.Owner);
        flow.Owner.Reopen();
        await before.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(flow.Owner));
        await Bm25OriginalPhaseRecoveryState.RequireRetainedAsync(flow.Owner, original, entry);
    }

    internal static async Task ReplayAsync(Bm25OriginalPhaseRecoveryFlow flow,
        Command<DatabaseCommand> original, Result<object> expected, byte[] entry)
    {
        var before = Bm25OriginalPhaseRecoveryState.Read(flow.Owner);
        var replay = flow.Publish(flow.Reissue(original));
        await ReplayResultAssertions.SameAsync(expected, replay);
        await ReplayResultAssertions.SameAsync(expected, flow.Owner.Database.ResolveOutcome(original));
        await ReplayResultAssertions.BytesAsync(OrleansSerialization.Serialize(expected.Get<CommitReceipt>()),
            OrleansSerialization.Serialize(replay.Get<CommitReceipt>()));
        await before.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(flow.Owner));
        await Bm25OriginalPhaseRecoveryState.RequireRetainedAsync(flow.Owner, original, entry);
    }

    internal static async Task SearchAsync(Bm25OriginalPhaseRecoveryFlow flow, bool expectedCurrent)
    {
        var owner = flow.Owner;
        var before = Bm25OriginalPhaseRecoveryState.Read(owner);
        var current = owner.Database.WithQueryView(Bm25OriginalPhaseRecoveryFlow.Subject,
            owner.Source.Partition, Bm25ColdInvalidationTrial.Collection, (view, principal, resource) =>
                owner.Database.ReadBm25CorpusCurrent(view, principal, resource, owner.Source.Partition,
                    Bm25ColdInvalidationTrial.Collection, Bm25ColdInvalidationTrial.Field,
                    TextProjectionProtocol.TokenizerVersion, ["needle"], owner.Source.CreateReadWork(Bm25OriginalPhaseRecoveryFlow.Token)));
        await Assert.That(current is not null).IsEqualTo(expectedCurrent);
        var rows = await new SearchEngine(owner.Database, UnitExecutionOptions.QueryExecution()).SearchAsync(
            Bm25OriginalPhaseRecoveryFlow.Subject, new(owner.Source.Partition,
                Bm25ColdInvalidationTrial.Collection, Bm25ColdInvalidationTrial.Field, "needle"), Bm25OriginalPhaseRecoveryFlow.Token);
        await Assert.That(rows).HasSingleItem();
        await Assert.That(rows[0].Document.Reference.Id).IsEqualTo("first");
        await Assert.That(rows[0].Document.Json).IsEqualTo(Bm25ColdInvalidationTrial.Json);
        await before.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(owner));
    }

    internal static async Task DeniedAsync(Bm25OriginalPhaseRecoveryFlow flow, Command<DatabaseCommand> original)
    {
        var before = Bm25OriginalPhaseRecoveryState.Read(flow.Owner);
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            new SearchEngine(flow.Owner.Database, UnitExecutionOptions.QueryExecution()).SearchAsync(
                Bm25OriginalPhaseRecoveryFlow.Subject, new(flow.Owner.Source.Partition,
                    Bm25ColdInvalidationTrial.Collection, Bm25ColdInvalidationTrial.Field, "needle"), Bm25OriginalPhaseRecoveryFlow.Token));
        await Assert.That(error!.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await before.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(flow.Owner));
        await PhaseDeniedAsync(flow, original);
    }

    internal static async Task PhaseDeniedAsync(Bm25OriginalPhaseRecoveryFlow flow, Command<DatabaseCommand> original)
    {
        var before = Bm25OriginalPhaseRecoveryState.Read(flow.Owner);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => flow.Reissue(original));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await before.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(flow.Owner));
    }
}
