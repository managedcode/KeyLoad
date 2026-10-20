using System.Security.Cryptography;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes MessagingValidationQueueRetries operations against the caller's database context.</summary>
public static class MessagingValidationQueueRetries
{
    internal static void ValidateQueueRetryDecisions(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        var payload = operation.Value!;
        var inputs = engine.ReadQueueRetryInputs(view, principal, operation);
        if (inputs.Count == QueueRetryProtocol.Initial)
        {
            if (!payload.RetryDecisions.IsEmpty)
            {
                throw Errors.Fail(ErrorCode.Conflict, QueueRetryProtocol.StaleDecision);
            }

            return;
        }

        var decisions = ReadQueueRetryDecisions(operation, payload);
        if (decisions.Items.Length != inputs.Count)
        {
            throw Errors.Fail(ErrorCode.Conflict, QueueRetryProtocol.StaleDecision);
        }

        for (var index = QueueRetryProtocol.Initial; index < inputs.Count; index++)
        {
            engine.RequireQueueRetryInput(operation, decisions.Items[index], inputs[index]);
        }
    }

    internal static QueueRetryDecisions ReadQueueRetryDecisions(Command<DatabaseCommand> operation, DatabaseCommand payload)
    {
        if (payload.RetryDecisions.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueRetryProtocol.MissingDecision);
        }

        var choices = OrleansSerialization.Deserialize<QueueRetryDecisions>(payload.RetryDecisions.Span);
        if (choices.Version != QueueRetryProtocol.Version || choices.EvaluatedAt != operation.Timestamp || choices.Items.IsDefaultOrEmpty)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueRetryProtocol.InvalidDecision);
        }

        return choices;
    }

    internal static void RequireQueueRetryInput(this DatabaseEngine engine, Command<DatabaseCommand> operation, QueueRetryDecision choice, QueueRetryInput input)
    {
        var metadata = input.Metadata;
        if (choice.Lane != input.Lane || choice.MessageId != metadata.Id || choice.ExpectedStateVersion != metadata.StateVersion || choice.ExpectedLeaseVersion != metadata.LeaseVersion || choice.DeliveryGeneration != metadata.DeliveryGeneration || choice.Attempts != metadata.Attempts || !choice.DueIndexKey.Span.SequenceEqual(input.DueIndexKey.Span) || !CryptographicOperations.FixedTimeEquals(choice.PolicyDigest.Span, SHA256.HashData(OrleansSerialization.Serialize(input.Policy))))
        {
            throw Errors.Fail(ErrorCode.Conflict, QueueRetryProtocol.StaleDecision);
        }

        if (choice.DelayMilliseconds < QueueRetryProtocol.Positive || choice.DelayMilliseconds > engine.QueueRetryCap(input.Policy, metadata.Attempts) || choice.DelayMilliseconds != engine.ChooseQueueRetryDelay(operation, input.Lane, input.Policy, metadata) || choice.RetryAt != operation.Timestamp.AddMilliseconds(choice.DelayMilliseconds))
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueRetryProtocol.InvalidDecision);
        }
    }

    internal static DateTimeOffset QueueRetryAt(this DatabaseEngine engine, Command<DatabaseCommand> operation, QueueLaneRef lane, QueuePolicy policy, MessageMetadata metadata, ReadOnlySpan<byte> dueKey)
    {
        if (policy.RetryJitter == QueueRetryJitter.None)
        {
            return operation.Timestamp.AddMilliseconds(engine.QueueRetryCap(policy, metadata.Attempts));
        }

        var payload = operation.Value!;
        var choices = ReadQueueRetryDecisions(operation, payload);
        foreach (var choice in choices.Items)
        {
            if (choice.Lane == lane && choice.MessageId == metadata.Id && choice.DueIndexKey.Span.SequenceEqual(dueKey))
            {
                return choice.RetryAt;
            }
        }

        throw Errors.Fail(ErrorCode.Corruption, QueueRetryProtocol.MissingDecision);
    }
}
