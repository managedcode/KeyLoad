using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.UnitTests.Features.Authorization;
using A = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionBoundReferenceNativeAssertions;
using D = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionBoundReferenceNativeData;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal static class CompositionBoundReferenceNativeFlow
{
    private const string IdColumn = "id";

    internal static Task RunAsync(bool typed, CancellationToken token) => SafeProjectorOperationDatabase.RunAsync(async owner =>
    {
        await ConfigureAsync(owner, typed);
        var from = CompositionBoundReferenceNativeReads.Reference(owner, D.From, D.First);
        var to = CompositionBoundReferenceNativeReads.Reference(owner, D.To, D.Second);
        var plainFrom = new EntityRef(owner.Source.Partition, D.From, D.First);
        var plainTo = new EntityRef(owner.Source.Partition, D.To, D.Second);
        var opaque = D.OpaquePayload(plainFrom, plainTo);
        var commands = ImmutableArray.CreateBuilder<CompositionBoundReferenceNativeCommand>();
        commands.Add(await CommitAsync(owner,
            [new PutDocument(D.From, D.First, D.FirstJson, 0), new PutDocument(D.To, D.Second, D.SecondJson, 0),
             new EnqueueMessage(D.Queue, D.Message, opaque)],
            [new("putDocument", D.From, D.First, 1), new("putDocument", D.To, D.Second, 1),
             new("enqueue", D.Queue, D.Message, 1)], false, [], token));
        commands.Add(await CommitAsync(owner, [new GraphToQueueMutation(D.ReverseQueue, D.EmptyGraph, plainFrom, D.ReversePrefix)],
            [], true, [from], token));
        commands.Add(await CommitAsync(owner, [Forward(typed, D.Graph, D.Queue, D.ForwardPrefix)],
            [new("upsertEdge", D.Graph, D.EdgeId, 1) { CompositionReferences = [from, to] }], true, [from, to], token));
        commands.Add(await CommitAsync(owner, [new GraphToQueueMutation(D.ReverseQueue, D.Graph, plainFrom, D.ReversePrefix)],
            [new("enqueue", D.ReverseQueue, D.DerivedId, 1) { CompositionReferences = [from, to] }], true, [from, to], token));
        commands.Add(await CommitAsync(owner, [Forward(typed, D.RoundGraph, D.ReverseQueue, D.RoundPrefix)],
            [new("upsertEdge", D.RoundGraph, D.RoundEdgeId, 1) { CompositionReferences = [from, to] }], true, [from, to], token));
        var state = new CompositionBoundReferenceNativeState(from, to, opaque, commands.ToImmutable());
        await VerifyAsync(owner, state, token);
        for (var cold = 0; cold < 2; cold++)
        {
            await A.UnchangedAsync(owner, owner.ReopenAsync);
            await VerifyAsync(owner, state, token);
        }
    }, token);

    private static QueueToGraph Forward(bool typed, string graph, string queue, string prefix)
        => new(graph, queue, prefix, 1, typed ? D.From : null, typed ? D.To : null);

    private static async Task ConfigureAsync(SafeProjectorOperationDatabase owner, bool typed)
    {
        foreach (var name in new[] { D.From, D.To })
        {
            var definition = new ResourceDefinition(name, ResourceKind.Collection, owner.Source.Partition.TransactionDomainId)
            {
                RelationalSchema = typed ? new(IdColumn, [new(IdColumn, RelationalColumnType.Text), new("name", RelationalColumnType.Text)]) : null
            };
            await A.EqualAsync(definition, owner.Source.Submit(OperationKind.ConfigureResource,
                new ConfigureResourceRequest(owner.Source.Partition.TenantId, owner.Source.Partition.DatabaseId, definition))
                .Get<ResourceDefinition>());
        }
        owner.Source.Configure(D.Graph, ResourceKind.Graph);
        owner.Source.Configure(D.RoundGraph, ResourceKind.Graph);
        owner.Source.Configure(D.EmptyGraph, ResourceKind.Graph);
        owner.Source.Configure(D.Queue, ResourceKind.WorkQueue);
        owner.Source.Configure(D.ReverseQueue, ResourceKind.WorkQueue);
    }

    private static async Task<CompositionBoundReferenceNativeCommand> CommitAsync(SafeProjectorOperationDatabase owner,
        ImmutableArray<Mutation> mutations, ImmutableArray<MutationReceipt> expectedMutations, bool derived, ImmutableArray<EntityRef> authorityReferences, CancellationToken token)
    {
        var request = new CommandRequest(Guid.NewGuid(), owner.Source.Partition, mutations);
        var work = new ReadExecutionBudget(owner.Database.OperationLimitsOptions, owner.Database.EvaluationClock, token);
        var operation = owner.Database.CreateCommand(OperationKind.Batch, request.CommandId, D.Root,
            owner.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request), work);
        var receipt = new CommitReceipt(request.CommandId, owner.NextToken(), expectedMutations, DurabilityProfile.ProcessDurable, 1);
        await A.ReceiptAsync(receipt, owner.Submit(operation).Get<CommitReceipt>());
        return new(request, operation, receipt, derived, authorityReferences);
    }

    private static Task VerifyAsync(SafeProjectorOperationDatabase owner, CompositionBoundReferenceNativeState state,
        CancellationToken token) => A.UnchangedAsync(owner, async () =>
    {
        await CompositionBoundReferenceNativeReads.VerifyAsync(owner, state, token);
        foreach (var command in state.Commands)
        {
            await A.ReceiptAsync(command.Receipt, owner.Submit(command.Operation).Get<CommitReceipt>());
            await A.ReceiptAsync(command.Receipt, owner.Database.ResolveOutcome(command.Operation).Get<CommitReceipt>());
            await A.OriginalAsync(owner, state, command);
        }
        await CompositionBoundReferenceNativeReads.VerifyAsync(owner, state, token);
    });
}
