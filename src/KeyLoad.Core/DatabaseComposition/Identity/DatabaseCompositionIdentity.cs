using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core.DatabaseComposition;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes DatabaseCompositionIdentity operations against the caller's database context.</summary>
public static class DatabaseCompositionIdentity
{
    internal const string MissingCompositionAuthority = "The composition outcome has no saved row authority.";
    internal const string InvalidCompositionAuthority = "The composition outcome row authority is invalid.";
    internal static global::ManagedCode.Communication.Result<CompositionOutcomeAuthority?> CaptureCompositionOutcome(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation, Result<object> result)
    {
        const int EmptyReverseLength = 0;
        if (Errors.Code(result.Problem).Value is not null || !TryCompositionInput(operation, out var partition, out var mutations))
        {
            return global::ManagedCode.Communication.Result<CompositionOutcomeAuthority?>.FailNotFound(nameof(CaptureCompositionOutcome));
        }

        var reverse = mutations.OfType<GraphToQueueMutation>().ToArray();
        if (!mutations.Any(mutation => mutation is QueueToGraph) && reverse.Length == EmptyReverseLength)
        {
            return global::ManagedCode.Communication.Result<CompositionOutcomeAuthority?>.FailNotFound(nameof(CaptureCompositionOutcome));
        }

        var receipt = result.Get<CommitReceipt>();
        var references = ImmutableArray.CreateBuilder<EntityRef>();
        var distinct = new HashSet<EntityRef>();
        foreach (var mutation in reverse)
        {
            // Successful expansion already authorized this visible start, even when it selected zero edges.
            // A later mutation may delete the row; retain that admission instead of re-reading its final row image.
            engine.Authorization.Require(principal, partition, mutation.Start.Collection, Capability.DocumentsRead);
            var start = engine.BindEntityReference(view, mutation.Start);
            DatabaseCompositionIdentity.AddCompositionReference(start, partition, distinct, references);
        }

        foreach (var effect in receipt.Mutations)
        {
            foreach (var reference in effect.CompositionReferences)
            {
                DatabaseCompositionIdentity.AddCompositionReference(reference, partition, distinct, references);
            }
        }

        return global::ManagedCode.Communication.Result<CompositionOutcomeAuthority?>.Succeed(((CompositionOutcomeAuthority?)(new(references.ToImmutable()))));
    }

    internal static ImmutableArray<EntityRef> CompositionMutationReferences(Mutation effect)
    {
        var references = effect switch
        {
            UpsertEdge edge => [edge.From, edge.To],
            EnqueueMessage message => DatabaseCompositionIdentity.MessageCompositionReferences(message),
            _ => throw Errors.Fail(ErrorCode.Corruption, InvalidCompositionAuthority)
        };
        foreach (var reference in references)
        {
            EntityReferenceLifetime.RequireBound(reference);
        }

        return references;
    }

    internal static ImmutableArray<EntityRef> MessageCompositionReferences(EnqueueMessage message)
    {
        try
        {
            var candidate = JsonSerializer.Deserialize<QueueGraphLink>(message.PayloadJson, JsonDefaults.Options) ?? throw Errors.Fail(ErrorCode.Corruption, InvalidCompositionAuthority);
            if (candidate.From is null)
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidCompositionAuthority);
            }

            var link = DatabaseCompositionExpansion.ReadLink(message.PayloadJson, candidate.From.Partition);
            return [link.From, link.To];
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidCompositionAuthority);
        }
    }

    internal static void ValidateCompositionOutcome(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation, StoredOutcome previous)
    {
        if (Errors.Code(previous.Result.Problem).Value is not null || !TryCompositionInput(operation, out var partition, out var mutations) || !mutations.Any(mutation => mutation is QueueToGraph or GraphToQueueMutation))
        {
            return;
        }

        var references = previous.CompositionAuthority?.References ?? throw Errors.Fail(ErrorCode.RecoveryRequired, MissingCompositionAuthority);
        if (references.IsDefault)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidCompositionAuthority);
        }

        var budget = new ReadExecutionBudget(CompositionTimeProvider.Instance);
        if (mutations.Any(mutation => mutation is QueueToGraph { FromCollection: not null }))
        {
            engine.ValidateTypedEndpointOutcomeReferences(view, principal, partition, references, budget);
            return;
        }

        foreach (var reference in references)
        {
            ValidateCompositionReference(reference, partition);
            EntityReferenceLifetime.RequireBound(reference);
            var document = engine.VisibleVertex(budget.CreateView(view), principal, reference);
            if (document.Reference != reference)
            {
                throw Errors.Fail(ErrorCode.Corruption, DatabaseCompositionTypedEndpointJoin.TypedJoinRecordMismatch);
            }
        }
    }

    internal static bool TryCompositionInput(Command<DatabaseCommand> operation, out PartitionRef partition, out ImmutableArray<Mutation> mutations)
    {
        if (operation.Value!.Kind == OperationKind.Batch)
        {
            var batch = OperationProtocol.Payload<CommandRequest>(operation);
            partition = batch.Partition;
            mutations = batch.Mutations;
            return true;
        }

        partition = default!;
        mutations = [];
        return false;
    }

    internal static void AddCompositionReference(EntityRef reference, PartitionRef partition, HashSet<EntityRef> distinct, ImmutableArray<EntityRef>.Builder references)
    {
        ValidateCompositionReference(reference, partition);
        EntityReferenceLifetime.RequireBound(reference);
        if (distinct.Add(reference))
        {
            references.Add(reference);
        }
    }

    internal static void ValidateCompositionReference(EntityRef reference, PartitionRef partition)
    {
        if (reference is null || reference.Partition != partition)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidCompositionAuthority);
        }

        JsonData.Identifier(reference.Collection);
        JsonData.Identifier(reference.Id);
    }
}
