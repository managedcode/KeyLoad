using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    // Only a discarded native preparation child may consume this unsigned candidate.
    // Keep original fixed across the sequence; existing original choices are never resampled.
    private ReplicatedOperation PrepareCompositionQueueRetry(IKeyValueView view,
        ReplicatedOperation original, PartitionRef partition, Mutation requested, NativeCommandPayload payload,
        List<QueueRetryDecision> choices)
    {
        var originalPayload = NativeSerialization.Deserialize<NativeCommandPayload>(original.NativePayload.Span);
        if (!originalPayload.RetryDecisions.IsEmpty)
        {
            return original;
        }
        if (requested is AdvanceQueueDeadline deadline
            && ReadQueueDeadlineRetryInput(view, partition, original.EvaluatedAt, deadline) is { } input)
        {
            if (choices.Count >= Limits.MaxBatchMutations)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, MutationCountBudgetMessage);
            }
            var delay = ChooseQueueRetryDelay(original, input.Lane, input.Policy, input.Metadata);
            choices.Add(new(input.Lane, input.Metadata.Id, input.Metadata.StateVersion, input.Metadata.LeaseVersion,
                input.Metadata.DeliveryGeneration, input.Metadata.Attempts, input.DueIndexKey,
                SHA256.HashData(NativeSerialization.Serialize(input.Policy)), delay,
                original.EvaluatedAt.AddMilliseconds(delay)));
        }
        if (choices.Count == QueueRetryProtocol.Initial)
        {
            return original;
        }
        var decisions = NativeSerialization.Serialize(new QueueRetryDecisions(QueueRetryProtocol.Version,
            original.EvaluatedAt, choices.ToImmutableArray()));
        RequireNativeBudget(decisions.Length);
        var candidate = NativeSerialization.Serialize(payload with
        {
            RetryDecisions = decisions,
            Authority = ReadOnlyMemory<byte>.Empty,
            Signature = ReadOnlyMemory<byte>.Empty
        });
        RequireNativeBudget(candidate.Length);
        return original with
        {
            NativePayload = candidate
        };
    }
}
