using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.CommandOutcomes.Execution;
using KeyLoad.Core.CommandOutcomes.Identity;
using KeyLoad.Core.Messaging;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes QueueRetryPreparation operations against the caller's database context.</summary>
public static class QueueRetryPreparation
{
    internal static Command<DatabaseCommand> PrepareQueueRetryOperation(this DatabaseEngine engine, Command<DatabaseCommand> operation) => engine.Store.Read(view => engine.PrepareQueueRetryInView(view, operation));
    internal static Command<DatabaseCommand> PrepareQueueRetryOperation(this DatabaseEngine engine, Command<DatabaseCommand> operation, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        var prepared = engine.Store.Read(raw => engine.PrepareQueueRetryInView(work.CreateView(raw), operation, preserveBudgetFailure: true));
        work.Check();
        return prepared;
    }

    internal static Command<DatabaseCommand> PrepareQueueRetryInView(this DatabaseEngine engine, IKeyValueView view, Command<DatabaseCommand> operation, bool preserveBudgetFailure = false)
    {
        if (operation.Value!.Kind is not (OperationKind.Delivery or OperationKind.Receive or OperationKind.Batch))
        {
            return operation;
        }

        var original = operation.Value!;
        if (!original.RetryDecisions.IsEmpty)
        {
            return operation;
        }

        if (operation.Value!.Kind == OperationKind.Batch)
        {
            var batch = OperationProtocol.Payload<CommandRequest>(operation);
            if (!batch.Mutations.IsDefault && !batch.Mutations.Any(static mutation => mutation is AdvanceQueueDeadline))
            {
                return operation;
            }
        }

        if (!engine.TryAuthorizeQueueRetry(view, operation, out var principal, preserveBudgetFailure) || principal is null)
        {
            return operation;
        }

        var scope = CommandOutcomePartitionIdentity.Resolve(operation);
        var selection = CommandOutcomeKeyResolver.Select(view, operation.UserId!, operation.CommandId, scope);
        if (selection.Outcome is { } outcome)
        {
            engine.ValidateRetainedQueueRetryAdmission(view, principal, operation, selection, outcome);
            return operation;
        }

        List<QueueRetryInput> inputs;
        try
        {
            inputs = engine.ReadQueueRetryInputs(view, principal, operation);
        }
        catch (KeyLoadException failure) when (failure.Code is not (ErrorCode.Corruption or ErrorCode.FormatUnsupported or ErrorCode.RecoveryRequired or ErrorCode.UnknownWriteOutcome) && (!preserveBudgetFailure || failure.Code != ErrorCode.BudgetExceeded))
        {
            return operation;
        }

        if (inputs.Count == QueueRetryProtocol.Initial)
        {
            return operation;
        }

        var choices = ImmutableArray.CreateBuilder<QueueRetryDecision>(inputs.Count);
        foreach (var input in inputs)
        {
            var delay = engine.ChooseQueueRetryDelay(operation, input.Lane, input.Policy, input.Metadata);
            choices.Add(new(input.Lane, input.Metadata.Id, input.Metadata.StateVersion, input.Metadata.LeaseVersion, input.Metadata.DeliveryGeneration, input.Metadata.Attempts, input.DueIndexKey, SHA256.HashData(OrleansSerialization.Serialize(input.Policy)), delay, operation.Timestamp.AddMilliseconds(delay)));
        }

        var decisions = OrleansSerialization.Serialize(new QueueRetryDecisions(QueueRetryProtocol.Version, operation.Timestamp, choices.MoveToImmutable()));
        var payload = operation.Value!;
        var prepared = engine.OwnCommand(operation);
        prepared.Value = payload with
        {
            RetryDecisions = decisions
        };
        return prepared;
    }
}
