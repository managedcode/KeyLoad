using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class Bm25OriginalExpiryAssertions
{
    internal static async Task PhaseAsync(ReplicatedOperation original, ReplicatedOperation actual)
        => await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(
            NativeSerialization.Serialize(NativeCommandPayload.Read<Bm25CorpusPublicationPhaseCommand>(original)),
            NativeSerialization.Serialize(NativeCommandPayload.Read<Bm25CorpusPublicationPhaseCommand>(actual)));

    internal static string[] Family(Bm25OriginalPhaseRecoveryFlow flow)
    {
        var owner = flow.Owner;
        var work = owner.Source.CreateReadWork(Bm25OriginalPhaseRecoveryFlow.Token);
        return owner.Store.Read(view =>
        {
            var page = work.Scan(view, Bm25CorpusKeys.Prefix(owner.Source.Partition),
                owner.Source.Database.Limits.MaxScanRecords);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The original corpus family exceeded its inspection bound.");
            }
            return page.Records.Select(row => Convert.ToHexString(row.Key.Span)
                + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
        });
    }

    internal static async Task FailureAsync(Bm25OriginalPhaseRecoveryFlow flow, ReplicatedOperation operation,
        OperationResult result, Bm25OriginalPhaseRecoveryState before, string[] family)
    {
        await Assert.That(result.Error).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(result.SafeDetail).IsEqualTo(Bm25CorpusProtocol.Changed);
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.NativeValue).IsNull();
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(family),
            NativeSerialization.Serialize(Family(flow)));
        var after = Bm25OriginalPhaseRecoveryState.Read(flow.Owner);
        await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
        await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
        await Assert.That(after.Position).IsGreaterThan(before.Position);
        await Assert.That(after.LastIndex).IsEqualTo(before.LastIndex + 1);
        await Assert.That(after.CommittedIndex).IsEqualTo(after.LastIndex);
        await Assert.That(after.Applied).IsEqualTo(after.LastIndex);
        await Assert.That(flow.Owner.Entry(operation.Id).Index).IsEqualTo(after.LastIndex);
        await PhaseAsync(operation, flow.Owner.Entry(operation.Id).Operation!);
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(result,
            flow.Owner.Database.ResolveSubmissionOutcome(operation));
    }

    internal static async Task ReplayAsync(Bm25OriginalPhaseRecoveryFlow flow, ReplicatedOperation original,
        OperationResult expected, byte[] entry, DateTimeOffset expiry)
    {
        var before = Bm25OriginalPhaseRecoveryState.Read(flow.Owner);
        var reissued = flow.Reissue(original);
        await Assert.That(reissued.EvaluatedAt >= expiry).IsTrue();
        await PhaseAsync(original, reissued);
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(expected, flow.Publish(reissued));
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(expected,
            flow.Owner.Database.ResolveSubmissionOutcome(original));
        await before.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(flow.Owner));
        await Bm25OriginalPhaseRecoveryState.RequireRetainedAsync(flow.Owner, original, entry);
    }
}
