using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.UnitTests.Features.Authorization;
using A = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionLinkLifetimeJsonAssertions;
using P = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionLinkLifetimeJsonProtocol;
using S = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionLinkLifetimeJsonSeed;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal static class CompositionLinkLifetimeJsonFlow
{
    internal static Task RunAsync(int malformed, CancellationToken token) => SafeProjectorOperationDatabase.RunAsync(async owner =>
    {
        var state = await S.CreateAsync(owner, token);
        var payload = P.Payload(state.From, state.To, malformed);
        var enqueue = S.Issue(owner, [new EnqueueMessage(P.Queue, P.Message, payload)], token);
        Command<DatabaseCommand> failed;
        if (malformed == P.Duplicate)
        {
            failed = enqueue;
        }
        else
        {
            await A.ReceiptAsync(new(enqueue.Id, owner.NextToken(), [new("enqueue", P.Queue, P.Message, 1)], DurabilityProfile.ProcessDurable, 1), owner.Submit(enqueue).Get<CommitReceipt>());
            failed = S.Issue(owner, [new QueueToGraph(P.Graph, P.Queue, P.Prefix, 1, P.From, P.To)], token);
        }
        var saved = malformed == P.Duplicate ? null : payload;
        var detail = malformed == P.Duplicate ? P.DuplicateProperty : P.InvalidLink;
        await A.ModelsAsync(owner, state, saved, false, token);
        var before = CompositionLinkLifetimeJsonConservation.Capture(owner, failed);
        await FailureAsync(owner, failed, detail);
        await before.RequireFailureAsync(owner, failed, detail);
        await A.ModelsAsync(owner, state, saved, false, token);
        await ReplayFailureAsync(owner, failed, detail);
        await HealthyAsync(owner, state, saved, token);
    }, token);

    private static async Task FailureAsync(SafeProjectorOperationDatabase owner, Command<DatabaseCommand> operation, string detail)
    {
        var failed = owner.Submit(operation);
        await A.EqualAsync(Result<object>.Fail(Errors.Problem(ErrorCode.Validation, detail)), failed);
        await Assert.That(failed.Value).IsNull();
        
    }

    private static Task ReplayFailureAsync(SafeProjectorOperationDatabase owner, Command<DatabaseCommand> operation, string detail)
        => A.UnchangedAsync(owner, async () =>
        {
            await FailureAsync(owner, operation, detail);
            var resolved = owner.Database.ResolveOutcome(operation);
            await A.EqualAsync(Result<object>.Fail(Errors.Problem(ErrorCode.Validation, detail)), resolved);
            await Assert.That(resolved.Value).IsNull();
        });

    private static async Task HealthyAsync(SafeProjectorOperationDatabase owner, CompositionLinkLifetimeJsonState state,
        string? saved, CancellationToken token)
    {
        var payload = P.Payload(state.From, state.To);
        var enqueue = S.Issue(owner, [new EnqueueMessage(P.HealthyQueue, P.Message, payload)], token);
        await A.ReceiptAsync(new(enqueue.Id, owner.NextToken(), [new("enqueue", P.HealthyQueue, P.Message, 1)], DurabilityProfile.ProcessDurable, 1), owner.Submit(enqueue).Get<CommitReceipt>());
        var forward = S.Issue(owner, [new QueueToGraph(P.Graph, P.HealthyQueue, P.Prefix, 1, P.From, P.To)], token);
        var expected = new CommitReceipt(forward.Id, owner.NextToken(), [new("upsertEdge", P.Graph, P.Edge, 1) { CompositionReferences = [state.From, state.To] }], DurabilityProfile.ProcessDurable, 1);
        await A.ReceiptAsync(expected, owner.Submit(forward).Get<CommitReceipt>());
        await A.ModelsAsync(owner, state, saved, true, token);
        await A.UnchangedAsync(owner, async () =>
        {
            await A.ReceiptAsync(expected, owner.Submit(forward).Get<CommitReceipt>());
            await A.ReceiptAsync(expected, owner.Database.ResolveOutcome(forward).Get<CommitReceipt>());
            await A.ModelsAsync(owner, state, saved, true, token);
        });
    }
}
