using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Search;

internal static class Bm25ConfiguredCountTrial
{
    private const string SecondSubject = "bm25-count-second-subject";

    internal static async Task RunAsync(Bm25OriginalPhaseRecoveryFlow flow, int maximumHeads)
    {
        var owner = flow.Owner;
        var second = flow.Configure(flow.Reader with
        {
            Id = SecondSubject
        });
        await Assert.That(second.PolicyEpoch).IsEqualTo(flow.Reader.PolicyEpoch);
        var first = flow.Capture(null);
        var firstExpected = Bm25OriginalPhaseRecoveryState.BeforeLocalPublish(owner.Database, owner.Source.Partition);
        var firstResult = flow.Publish(first);
        await RequireStoredAsync(flow, first, firstResult, firstExpected);
        var originalEntry = NativeSerialization.Serialize(owner.PhaseEntry(first));
        var next = CaptureSecond(flow);
        var replacement = flow.Capture(first.CommandId);
        var healthy = flow.Capture(replacement.CommandId);
        var before = Bm25OriginalPhaseRecoveryState.Read(owner);
        var familyBefore = Family(flow);
        var limits = owner.Source.Database.Limits with
        {
            MaxScanRecords = maximumHeads
        };
        owner.Reopen(limits);
        await before.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(owner));
        await Assert.That(owner.Database.Limits).IsEqualTo(limits);
        var nextExpected = Bm25OriginalPhaseRecoveryState.BeforeLocalPublish(owner.Database, owner.Source.Partition);
        var result = flow.Publish(next);
        if (maximumHeads == 1)
        {
            await RequireQuotaFailureAsync(flow, next, result, before, familyBefore);
        }
        else
        {
            await RequireStoredAsync(flow, next, result, nextExpected);
        }
        await Assert.That(Family(flow).Length).IsEqualTo(maximumHeads);
        await Bm25OriginalPhaseRecoveryState.RequireRetainedAsync(owner, first, originalEntry);
        await RequireReplayAsync(flow, next, result);
        var replacementExpected = Bm25OriginalPhaseRecoveryState.BeforeLocalPublish(owner.Database, owner.Source.Partition);
        await RequireStoredAsync(flow, replacement, flow.Publish(replacement), replacementExpected);
        var healthyExpected = Bm25OriginalPhaseRecoveryState.BeforeLocalPublish(owner.Database, owner.Source.Partition);
        var healthyResult = flow.Publish(healthy);
        await RequireStoredAsync(flow, healthy, healthyResult, healthyExpected);
        await Assert.That(Family(flow).Length).IsEqualTo(maximumHeads);
        var final = Bm25OriginalPhaseRecoveryState.Read(owner);
        owner.Reopen(limits);
        await final.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(owner));
        await RequireReplayAsync(flow, healthy, healthyResult);
        await RequireReplayAsync(flow, next, result);
    }

    private static Command<DatabaseCommand> CaptureSecond(Bm25OriginalPhaseRecoveryFlow flow)
        => Bm25CorpusCapture.Create(flow.Owner.Database, Bm25OriginalPhaseRecoveryFlow.Publisher, SecondSubject,
            new(flow.Owner.Source.Partition, "orders", "/text", "needle"), Guid.NewGuid(), null,
            flow.Owner.Database.EvaluationClock.GetUtcNow().Add(UnitRoutingOptions.Routing().Value.RequestLifetime),
            flow.Owner.Source.CreateReadWork(Bm25OriginalPhaseRecoveryFlow.Token), UnitExecutionOptions.QueryExecution());

    private static string[] Family(Bm25OriginalPhaseRecoveryFlow flow)
    {
        var owner = flow.Owner;
        var work = owner.Source.CreateReadWork(Bm25OriginalPhaseRecoveryFlow.Token);
        return owner.Store.Read(view =>
        {
            var rows = new List<string>();
            var scan = view.VisitRange(Bm25CorpusKeys.Prefix(owner.Source.Partition),
                owner.Source.Database.Limits.MaxScanRecords, (key, value) =>
                {
                    rows.Add(Convert.ToHexString(key) + ":" + Convert.ToHexString(value));
                    return true;
                },
                observer: work.ChargeBytes, cancellationToken: Bm25OriginalPhaseRecoveryFlow.Token);
            if (scan.HasMore)
            {
                throw new InvalidOperationException("The complete original corpus family exceeded its inspection bound.");
            }
            return rows.ToArray();
        });
    }

    private static byte[] Outcome(Bm25OriginalPhaseRecoveryFlow flow, Command<DatabaseCommand> operation)
    {
        var work = flow.Owner.Source.CreateReadWork(Bm25OriginalPhaseRecoveryFlow.Token);
        return flow.Owner.Store.Read(view =>
        {
            var bounded = work.CreateView(view);
            var selected = CommandOutcomeKeyResolver.Select(bounded, operation.UserId!, operation.CommandId,
                CommandOutcomePartitionIdentity.Resolve(operation));
            return bounded.ReadOwnedValue(selected.Key)
                ?? throw new InvalidOperationException("The original publication outcome is missing.");
        });
    }

    private static async Task RequireQuotaFailureAsync(Bm25OriginalPhaseRecoveryFlow flow,
        Command<DatabaseCommand> operation, Result<object> result, Bm25OriginalPhaseRecoveryState before, string[] familyBefore)
    {
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(result.Problem?.Detail).IsEqualTo(Bm25CorpusProtocol.Capacity);

        await Assert.That(result.Value).IsNull();
        await NativeReplayResultAssertions.BytesAsync(NativeSerialization.Serialize(familyBefore),
            NativeSerialization.Serialize(Family(flow)));
        var after = Bm25OriginalPhaseRecoveryState.Read(flow.Owner);
        await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
        await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
        await Assert.That(after.Position).IsEqualTo(checked(before.Position + 1L));
        await Assert.That(after.Applied).IsEqualTo(before.Applied);
        await Assert.That(flow.Owner.PhaseEntry(operation).CanonicalSequence).IsEqualTo(0L);
        await NativeReplayResultAssertions.SameAsync(result, flow.Owner.Database.ResolveSubmissionOutcome(operation));
        await Assert.That(Outcome(flow, operation).Length).IsGreaterThan(0);
    }

    private static async Task RequireStoredAsync(Bm25OriginalPhaseRecoveryFlow flow,
        Command<DatabaseCommand> operation, Result<object> result, (CommitToken Token, long Applied) expected)
    {
        await Assert.That(Errors.Code(result.Problem)).IsNull();
        var phase = NativeSerialization.Deserialize<Bm25CorpusPublicationPhaseCommand>((operation).Value!.Input.Span);
        await Assert.That(phase.Current.Statistics.DocumentCount).IsEqualTo(1);
        await Assert.That(phase.Current.Statistics.TotalLength).IsEqualTo(1L);
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(operation.CommandId);
        await Assert.That(receipt.Token).IsEqualTo(expected.Token);
        await Assert.That(flow.Owner.PhaseEntry(operation).CanonicalSequence).IsEqualTo(0L);
        await Assert.That(flow.Owner.Database.LastApplied).IsEqualTo(expected.Applied);
        await Assert.That(flow.Owner.Store.Position).IsEqualTo(expected.Token.Position);
        var expectedReceipt = new CommitReceipt(operation.CommandId, expected.Token, [],
            flow.Owner.Database.Durability, flow.Owner.Database.AcknowledgedPersistedCopies);
        await NativeReplayResultAssertions.BytesAsync(NativeSerialization.Serialize(expectedReceipt), NativeSerialization.Serialize(receipt));
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(flow.Owner.Store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(flow.Owner.Source.Partition.AtomicPartitionId);
        await Assert.That(receipt.Durability).IsEqualTo(flow.Owner.Database.Durability);
        var work = flow.Owner.Source.CreateReadWork(Bm25OriginalPhaseRecoveryFlow.Token);
        var actual = flow.Owner.Store.Read(view => work.CreateView(view).ReadOwnedValue(Bm25CorpusKeys.Current(phase.Current)))
            ?? throw new InvalidOperationException("The actual published corpus head is missing.");
        await NativeReplayResultAssertions.BytesAsync(NativeSerialization.Serialize(phase.Current), actual);
        await NativeReplayResultAssertions.SameAsync(result, flow.Owner.Database.ResolveSubmissionOutcome(operation));
    }

    private static async Task RequireReplayAsync(Bm25OriginalPhaseRecoveryFlow flow,
        Command<DatabaseCommand> original, Result<object> expected)
    {
        var before = Bm25OriginalPhaseRecoveryState.Read(flow.Owner);
        var raw = Outcome(flow, original);
        var entry = NativeSerialization.Serialize(flow.Owner.PhaseEntry(original));
        var issuance = Assert.ThrowsExactly<KeyLoadException>(() => flow.Reissue(original));
        await Assert.That(issuance.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(issuance.Message).IsEqualTo(ResourceEffectAdmissionProtocol.Capacity);
        await before.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(flow.Owner));
        await NativeReplayResultAssertions.SameAsync(expected, flow.Publish(original));
        await before.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(flow.Owner));
        await NativeReplayResultAssertions.BytesAsync(raw, Outcome(flow, original));
        await Bm25OriginalPhaseRecoveryState.RequireRetainedAsync(flow.Owner, original, entry);
    }
}
