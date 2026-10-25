using KeyLoad.Core.Search;
using KeyLoad.UnitTests.ResourceExecution;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Search;

internal static class Bm25OriginalExpiryAssertions
{
    internal static async Task PhaseAsync(Command<DatabaseCommand> original, Command<DatabaseCommand> actual)
        => await ReplayResultAssertions.BytesAsync(
            OrleansSerialization.Serialize(OrleansSerialization.Deserialize<Bm25CorpusPublicationPhaseCommand>((original).Value!.Input.Span)),
            OrleansSerialization.Serialize(OrleansSerialization.Deserialize<Bm25CorpusPublicationPhaseCommand>((actual).Value!.Input.Span)));

    internal static string[] Family(Bm25OriginalPhaseRecoveryFlow flow)
    {
        var owner = flow.Owner;
        var work = owner.Source.CreateReadWork(Bm25OriginalPhaseRecoveryFlow.Token);
        return owner.Store.Read(view =>
        {
            var page = work.Scan(view, Bm25CorpusKeys.Prefix(owner.Source.Partition), view.MaximumRangeRecords);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The original corpus family exceeded its inspection bound.");
            }
            return page.Records.Select(row => Convert.ToHexString(row.Key.Span)
                + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
        });
    }

    internal static async Task FailureAsync(Bm25OriginalPhaseRecoveryFlow flow, Command<DatabaseCommand> operation,
        Result<object> result, Bm25OriginalPhaseRecoveryState before, string[] family)
    {
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(result.Problem?.Detail).IsEqualTo(Bm25CorpusProtocol.Changed);

        await Assert.That(result.Value).IsNull();
        await ReplayResultAssertions.BytesAsync(OrleansSerialization.Serialize(family),
            OrleansSerialization.Serialize(Family(flow)));
        var after = Bm25OriginalPhaseRecoveryState.Read(flow.Owner);
        await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
        await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
        await Assert.That(after.Position).IsEqualTo(checked(before.Position + 1L));
        await Assert.That(after.Applied).IsEqualTo(before.Applied);
        await Assert.That(flow.Owner.Entry(operation.CommandId).CanonicalSequence).IsEqualTo(0L);
        await PhaseAsync(operation, flow.Owner.Entry(operation.CommandId).PreparedCommand!);
        await ReplayResultAssertions.SameAsync(result,
            flow.Owner.Database.ResolveSubmissionOutcome(operation));
    }

    internal static async Task ReplayAsync(Bm25OriginalPhaseRecoveryFlow flow, Command<DatabaseCommand> original,
        Result<object> expected, byte[] entry, DateTimeOffset expiry)
    {
        var before = Bm25OriginalPhaseRecoveryState.Read(flow.Owner);
        var reissued = flow.Reissue(original);
        await Assert.That(new DateTimeOffset(reissued.Timestamp) >= expiry).IsTrue();
        await PhaseAsync(original, reissued);
        await ReplayResultAssertions.SameAsync(expected, flow.Publish(reissued));
        await ReplayResultAssertions.SameAsync(expected,
            flow.Owner.Database.ResolveSubmissionOutcome(original));
        await before.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(flow.Owner));
        await Bm25OriginalPhaseRecoveryState.RequireRetainedAsync(flow.Owner, original, entry);
    }
}
