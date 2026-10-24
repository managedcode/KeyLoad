using KeyLoad.UnitTests.Authorization;
using ManagedCode.Communication.Commands;
using A = KeyLoad.UnitTests.DatabaseComposition.CompositionBoundReferenceAssertions;
using D = KeyLoad.UnitTests.DatabaseComposition.CompositionReverseFinalImageData;

namespace KeyLoad.UnitTests.DatabaseComposition;

internal static class CompositionReverseFinalImageFlow
{
    internal static Task RunAsync(CancellationToken token) => SafeProjectorOperationDatabase.RunAsync(async owner =>
    {
        owner.Source.Configure(D.Collection, ResourceKind.Collection);
        owner.Source.Configure(D.Graph, ResourceKind.Graph);
        owner.Source.Configure(D.Queue, ResourceKind.WorkQueue);
        var bound = CompositionBoundReferenceReads.Reference(owner, D.Collection, D.Id);
        var seedRequest = new CommandRequest(Guid.NewGuid(), owner.Source.Partition,
            [new PutDocument(D.Collection, D.Id, D.Json, 0)]);
        var seed = Issue(owner, seedRequest, token, out _);
        var seedReceipt = new CommitReceipt(seed.CommandId, owner.NextToken(), [new("putDocument", D.Collection, D.Id, 1)], DurabilityProfile.ProcessDurable, 1);
        await A.ReceiptAsync(seedReceipt, owner.Submit(seed).Get<CommitReceipt>());
        var request = new CommandRequest(Guid.NewGuid(), owner.Source.Partition,
            [new GraphToQueueMutation(D.Queue, D.Graph, bound with { Lifetime = null }, D.Prefix),
             new DeleteDocument(D.Collection, D.Id, 1)]);
        var original = Issue(owner, request, token, out var work);
        var receipt = new CommitReceipt(original.CommandId, owner.NextToken(), [new("deleteDocument", D.Collection, D.Id, 2)], DurabilityProfile.ProcessDurable, 1);
        var position = owner.Store.Position;
        await A.ReceiptAsync(receipt, owner.Submit(original).Get<CommitReceipt>());
        await Assert.That(owner.Store.Position).IsEqualTo(position + 1);
        await Assert.That(owner.ReadCanonicalCut().AppliedPosition).IsEqualTo(receipt.Token.Position);
        await Assert.That(owner.Database.LastApplied).IsEqualTo(receipt.Token.Position);
        var state = new CompositionReverseFinalImageState(bound, request, original, receipt, seed, seedReceipt);
        await A.UnchangedAsync(owner, async () =>
        {
            await CompositionReverseFinalImageAssertions.RefusalAsync(owner.Database.ResolveOutcome(original));
            var denied = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            {
                _ = owner.Database.ResolveSubmissionOutcome(original, work);
                return Task.CompletedTask;
            });
            ArgumentNullException.ThrowIfNull(denied);
            await Assert.That(denied.Code).IsEqualTo(ErrorCode.NotFound);
            await Assert.That(denied.Message).IsEqualTo(D.Unavailable);
        });
        await VerifyAsync(owner, state);
        for (var cold = 0; cold < 2; cold++)
        {
            await A.UnchangedAsync(owner, owner.ReopenAsync);
            await VerifyAsync(owner, state);
        }
    }, token);

    private static Command<DatabaseCommand> Issue(SafeProjectorOperationDatabase owner, CommandRequest request,
        CancellationToken token, out ReadExecutionBudget work)
    {
        work = new(owner.Database.OperationLimitsOptions, owner.Database.EvaluationClock, token);
        return owner.Database.CreateCommand(OperationKind.Batch, request.CommandId, D.Root,
            owner.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(request), work);
    }

    private static Task VerifyAsync(SafeProjectorOperationDatabase owner, CompositionReverseFinalImageState state)
        => A.UnchangedAsync(owner, async () =>
        {
            await CompositionReverseFinalImageAssertions.StateAsync(owner, state);
            await CompositionReverseFinalImageAssertions.RefusalAsync(owner.Submit(state.Original));
            await CompositionReverseFinalImageAssertions.RefusalAsync(owner.Database.ResolveOutcome(state.Original));
            await CompositionReverseFinalImageAssertions.StateAsync(owner, state);
        });
}
