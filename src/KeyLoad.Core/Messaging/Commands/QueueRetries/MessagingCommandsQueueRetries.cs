using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Messaging;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes MessagingCommandsQueueRetries operations against the caller's database context.</summary>
public static class MessagingCommandsQueueRetries
{
    // Only a discarded preparation child consumes these provisional retry choices.
    // Keep original fixed across the sequence; existing original choices are never resampled.
    internal static Command<DatabaseCommand> PrepareCompositionQueueRetry(this DatabaseEngine engine, IKeyValueView view, Command<DatabaseCommand> original, PartitionRef partition, Mutation requested, DatabaseCommand payload, List<QueueRetryDecision> choices)
    {
        var originalPayload = original.Value!;
        if (!originalPayload.RetryDecisions.IsEmpty)
        {
            return original;
        }

        if (requested is AdvanceQueueDeadline deadline && (engine.ReadQueueDeadlineRetryInput(view, partition, original.Timestamp, deadline)).Value is { } input)
        {
            var delay = engine.ChooseQueueRetryDelay(original, input.Lane, input.Policy, input.Metadata);
            choices.Add(new(input.Lane, input.Metadata.Id, input.Metadata.StateVersion, input.Metadata.LeaseVersion, input.Metadata.DeliveryGeneration, input.Metadata.Attempts, input.DueIndexKey, SHA256.HashData(OrleansSerialization.Serialize(input.Policy)), delay, original.Timestamp.AddMilliseconds(delay)));
        }

        if (choices.Count == QueueRetryProtocol.Initial)
        {
            return original;
        }

        var decisions = OrleansSerialization.Serialize(new QueueRetryDecisions(QueueRetryProtocol.Version, original.Timestamp, choices.ToImmutableArray()));
        var candidate = engine.OwnCommand(original);
        candidate.Value = payload with
        {
            RetryDecisions = decisions
        };
        return candidate;
    }
}
