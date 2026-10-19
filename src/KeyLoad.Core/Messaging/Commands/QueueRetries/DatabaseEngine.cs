using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    // Only a discarded preparation child consumes these provisional retry choices.
    // Keep original fixed across the sequence; existing original choices are never resampled.
    private Command<DatabaseCommand> PrepareCompositionQueueRetry(IKeyValueView view,
        Command<DatabaseCommand> original, PartitionRef partition, Mutation requested, DatabaseCommand payload,
        List<QueueRetryDecision> choices)
    {
        var originalPayload = original.Value!;
        if (!originalPayload.RetryDecisions.IsEmpty)
        {
            return original;
        }
        if (requested is AdvanceQueueDeadline deadline
            && ReadQueueDeadlineRetryInput(view, partition, original.Timestamp, deadline) is { } input)
        {
            if (choices.Count >= Limits.MaxBatchMutations)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, MutationCountBudgetMessage);
            }
            var delay = ChooseQueueRetryDelay(original, input.Lane, input.Policy, input.Metadata);
            choices.Add(new(input.Lane, input.Metadata.Id, input.Metadata.StateVersion, input.Metadata.LeaseVersion,
                input.Metadata.DeliveryGeneration, input.Metadata.Attempts, input.DueIndexKey,
                SHA256.HashData(OrleansSerialization.Serialize(input.Policy)), delay,
                original.Timestamp.AddMilliseconds(delay)));
        }
        if (choices.Count == QueueRetryProtocol.Initial)
        {
            return original;
        }
        var decisions = OrleansSerialization.Serialize(new QueueRetryDecisions(QueueRetryProtocol.Version,
            original.Timestamp, choices.ToImmutableArray()));
        RequireBudget(decisions.Length);
        var candidate = OwnCommand(original);
        candidate.Value = payload with { RetryDecisions = decisions };
        RequireBudget(OrleansSerialization.Measure(candidate));
        return candidate;
    }
}
