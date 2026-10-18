using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core.Features.DatabaseComposition;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int BaseReferencesPerMutation = 2;

    private const string MissingCompositionAuthority = "The composition outcome has no saved row authority.";
    private const string InvalidCompositionAuthority = "The composition outcome row authority is invalid.";
    private CompositionOutcomeAuthority? CaptureCompositionOutcome(IKeyValueView view, PrincipalRecord principal,
        Command<DatabaseCommand> operation, Result<object> result)
    {
        const int EmptyReverseLength = 0;

        if (Errors.Code(result.Problem) is not null || !TryCompositionInput(operation, out var partition, out var mutations))
        {
            return null;
        }
        var reverse = mutations.OfType<GraphToQueueMutation>().ToArray();
        if (!mutations.Any(mutation => mutation is QueueToGraph) && reverse.Length == EmptyReverseLength)
        {
            return null;
        }
        var receipt = result.Get<CommitReceipt>();
        var references = ImmutableArray.CreateBuilder<EntityRef>();
        var distinct = new HashSet<EntityRef>();
        foreach (var mutation in reverse)
        {
            // Successful expansion already authorized this visible start, even when it selected zero edges.
            // A later mutation may delete the row; retain that admission instead of re-reading its final row image.
            Authorization.Require(principal, partition, mutation.Start.Collection, Capability.DocumentsRead);
            var start = BindEntityReference(view, mutation.Start);
            AddCompositionReference(start, partition, distinct, references);
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

    private ImmutableArray<EntityRef> CompositionMutationReferences(Mutation effect)
    {
        var references = effect switch
        {
            UpsertEdge edge => [edge.From, edge.To],
            EnqueueMessage message => MessageCompositionReferences(message),
            _ => throw Errors.Fail(ErrorCode.Corruption, InvalidCompositionAuthority)
        };
        foreach (var reference in references)
        {
            EntityReferenceLifetime.RequireBound(reference);
        }
        return references;
    }

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
        Command<DatabaseCommand> operation, StoredOutcome previous)
    {
        if (Errors.Code(previous.Result.Problem) is not null || !TryCompositionInput(operation, out var partition, out var mutations)
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
        var budget = new ReadExecutionBudget(OperationLimitsOptions, CompositionTimeProvider.Instance);
        if (mutations.Any(mutation => mutation is QueueToGraph { FromCollection: not null }))
        {
            ValidateTypedEndpointOutcomeReferences(view, principal, partition, references, budget);
            return;
        }
        foreach (var reference in references)
        {
            ValidateCompositionReference(reference, partition);
            EntityReferenceLifetime.RequireBound(reference);
            var document = VisibleVertex(budget.CreateView(view), principal, reference);
            if (document.Reference != reference)
            {
                throw Errors.Fail(ErrorCode.Corruption, TypedJoinRecordMismatch);
            }
        }
    }

    private static bool TryCompositionInput(Command<DatabaseCommand> operation, out PartitionRef partition,
        out ImmutableArray<Mutation> mutations)
    {
        if (operation.Value!.Kind == OperationKind.Batch)
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
        const int MaxBatchMutationsScaleFactor = 3;

        ValidateCompositionReference(reference, partition);
        EntityReferenceLifetime.RequireBound(reference);
        if (distinct.Add(reference))
        {
            if (references.Count >= checked(MaxBatchMutationsScaleFactor * Limits.MaxBatchMutations))
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
        => checked(BaseReferencesPerMutation * Limits.MaxBatchMutations + mutations.Count(mutation => mutation is GraphToQueueMutation));
}
