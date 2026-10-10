using System.Security.Cryptography;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void ValidateQueueRetryDecisions(IKeyValueView view, PrincipalRecord principal, ReplicatedOperation operation)
    {
        var payload = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);
        var inputs = ReadQueueRetryInputs(view, principal, operation);
        if (inputs.Count == QueueRetryProtocol.Initial)
        {
            if (!payload.RetryDecisions.IsEmpty)
            { throw Errors.Fail(ErrorCode.Conflict, QueueRetryProtocol.StaleDecision); }
            return;
        }
        var decisions = ReadQueueRetryDecisions(operation, payload);
        if (decisions.Items.Length != inputs.Count)
        { throw Errors.Fail(ErrorCode.Conflict, QueueRetryProtocol.StaleDecision); }
        for (var index = QueueRetryProtocol.Initial; index < inputs.Count; index++)
        { RequireQueueRetryInput(operation, decisions.Items[index], inputs[index]); }
    }

    private static QueueRetryDecisions ReadQueueRetryDecisions(ReplicatedOperation operation, NativeCommandPayload payload)
    {
        if (payload.RetryDecisions.IsEmpty)
        { throw Errors.Fail(ErrorCode.Corruption, QueueRetryProtocol.MissingDecision); }
        var choices = NativeSerialization.Deserialize<QueueRetryDecisions>(payload.RetryDecisions.Span);
        if (choices.Version != QueueRetryProtocol.Version || choices.EvaluatedAt != operation.EvaluatedAt || choices.Items.IsDefaultOrEmpty)
        { throw Errors.Fail(ErrorCode.Corruption, QueueRetryProtocol.InvalidDecision); }
        return choices;
    }

    private void RequireQueueRetryInput(ReplicatedOperation operation, QueueRetryDecision choice, QueueRetryInput input)
    {
        var metadata = input.Metadata;
        if (choice.Lane != input.Lane || choice.MessageId != metadata.Id || choice.ExpectedStateVersion != metadata.StateVersion
            || choice.ExpectedLeaseVersion != metadata.LeaseVersion || choice.DeliveryGeneration != metadata.DeliveryGeneration
            || choice.Attempts != metadata.Attempts || !choice.DueIndexKey.Span.SequenceEqual(input.DueIndexKey.Span)
            || !CryptographicOperations.FixedTimeEquals(choice.PolicyDigest.Span, SHA256.HashData(NativeSerialization.Serialize(input.Policy))))
        { throw Errors.Fail(ErrorCode.Conflict, QueueRetryProtocol.StaleDecision); }
        if (choice.DelayMilliseconds < QueueRetryProtocol.Positive || choice.DelayMilliseconds > QueueRetryCap(input.Policy, metadata.Attempts)
            || choice.RetryAt != operation.EvaluatedAt.AddMilliseconds(choice.DelayMilliseconds))
        { throw Errors.Fail(ErrorCode.Corruption, QueueRetryProtocol.InvalidDecision); }
    }

    private DateTimeOffset QueueRetryAt(ReplicatedOperation operation, QueueLaneRef lane, QueuePolicy policy,
        MessageMetadata metadata, ReadOnlySpan<byte> dueKey)
    {
        if (policy.RetryJitter == QueueRetryJitter.None)
        { return operation.EvaluatedAt.AddMilliseconds(QueueRetryCap(policy, metadata.Attempts)); }
        var payload = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);
        var choices = ReadQueueRetryDecisions(operation, payload);
        foreach (var choice in choices.Items)
        {
            if (choice.Lane == lane && choice.MessageId == metadata.Id && choice.DueIndexKey.Span.SequenceEqual(dueKey))
            { return choice.RetryAt; }
        }
        throw Errors.Fail(ErrorCode.Corruption, QueueRetryProtocol.MissingDecision);
    }
}
