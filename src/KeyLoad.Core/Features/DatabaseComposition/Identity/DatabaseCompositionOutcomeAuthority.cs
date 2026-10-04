using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core.Features.DatabaseComposition;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string MissingCompositionAuthority = "The composition outcome has no saved row authority.";
    private const string InvalidCompositionAuthority = "The composition outcome row authority is invalid.";
    private CompositionOutcomeAuthority? CaptureCompositionOutcome(ReplicatedOperation operation, OperationResult result)
    {
        if (result.Error is not null || !TryCompositionInput(operation, out var partition, out var mutations))
        {
            return null;
        }
        var reverse = mutations.OfType<GraphToQueueMutation>().ToArray();
        if (!mutations.Any(mutation => mutation is QueueToGraph) && reverse.Length == 0)
        {
            return null;
        }
        var receipt = result.Get<CommitReceipt>();
        var references = ImmutableArray.CreateBuilder<EntityRef>();
        var distinct = new HashSet<EntityRef>();
        foreach (var mutation in reverse)
        {
            AddCompositionReference(mutation.Start, partition, distinct, references);
        }
        foreach (var effect in receipt.Mutations)
        {
            foreach (var reference in effect.CompositionReferences)
            {
                AddCompositionReference(reference, partition, distinct, references);
            }
        }
        return new(references.ToImmutable());
    }

    private ImmutableArray<EntityRef> CompositionMutationReferences(Mutation effect) => effect switch
    {
        UpsertEdge edge => [edge.From, edge.To],
        EnqueueMessage message => MessageCompositionReferences(message),
        _ => throw Errors.Fail(ErrorCode.Corruption, InvalidCompositionAuthority)
    };

    private ImmutableArray<EntityRef> MessageCompositionReferences(EnqueueMessage message)
    {
        try
        {
            var candidate = JsonSerializer.Deserialize<QueueGraphLink>(message.PayloadJson, JsonDefaults.Options)
                ?? throw Errors.Fail(ErrorCode.Corruption, InvalidCompositionAuthority);
            if (candidate.From is null)
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidCompositionAuthority);
            }
            var link = ReadLink(message.PayloadJson, candidate.From.Partition);
            return [link.From, link.To];
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidCompositionAuthority);
        }
    }

    private void ValidateCompositionOutcome(IKeyValueView view, PrincipalRecord principal,
        ReplicatedOperation operation, StoredOutcome previous)
    {
        if (previous.Result.Error is not null || !TryCompositionInput(operation, out var partition, out var mutations)
            || !mutations.Any(mutation => mutation is QueueToGraph or GraphToQueueMutation))
        {
            return;
        }
        var references = previous.CompositionAuthority?.References
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, MissingCompositionAuthority);
        if (references.IsDefault || references.Length > MaximumSavedCompositionReferences(mutations))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidCompositionAuthority);
        }
        var budget = new ReadExecutionBudget(Limits, CompositionTimeProvider.Instance);
        foreach (var reference in references)
        {
            ValidateCompositionReference(reference, partition);
            VisibleVertex(view, principal, reference, budget);
        }
    }

    private static bool TryCompositionInput(ReplicatedOperation operation, out PartitionRef partition,
        out ImmutableArray<Mutation> mutations)
    {
        if (operation.Kind == OperationKind.Batch)
        {
            var batch = Payload<CommandRequest>(operation);
            partition = batch.Partition;
            mutations = batch.Mutations;
            return true;
        }
        partition = default!;
        mutations = [];
        return false;
    }

    private void AddCompositionReference(EntityRef reference, PartitionRef partition,
        HashSet<EntityRef> distinct, ImmutableArray<EntityRef>.Builder references)
    {
        ValidateCompositionReference(reference, partition);
        if (distinct.Add(reference))
        {
            if (references.Count >= checked(3 * Limits.MaxBatchMutations))
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidCompositionAuthority);
            }
            references.Add(reference);
        }
    }

    private static void ValidateCompositionReference(EntityRef reference, PartitionRef partition)
    {
        if (reference is null || reference.Partition != partition)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidCompositionAuthority);
        }
        JsonData.Identifier(reference.Collection);
        JsonData.Identifier(reference.Id);
    }

    private int MaximumSavedCompositionReferences(ImmutableArray<Mutation> mutations)
        => checked(2 * Limits.MaxBatchMutations + mutations.Count(mutation => mutation is GraphToQueueMutation));
}
