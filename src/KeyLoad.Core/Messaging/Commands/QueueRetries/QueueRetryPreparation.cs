using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.CommandOutcomes.Execution;
using KeyLoad.Core.Features.CommandOutcomes.Identity;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal Command<DatabaseCommand> PrepareQueueRetryOperation(Command<DatabaseCommand> operation)
        => Store.Read(view => PrepareQueueRetryInView(view, operation));

    internal Command<DatabaseCommand> PrepareQueueRetryOperation(Command<DatabaseCommand> operation, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        var prepared = Store.Read(raw => PrepareQueueRetryInView(work.CreateView(raw), operation, preserveBudgetFailure: true));
        work.Check();
        return prepared;
    }

    private Command<DatabaseCommand> PrepareQueueRetryInView(IKeyValueView view, Command<DatabaseCommand> operation, bool preserveBudgetFailure = false)
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
            var batch = Payload<CommandRequest>(operation);
            if (!batch.Mutations.IsDefault && !batch.Mutations.Any(static mutation => mutation is AdvanceQueueDeadline))
            {
                return operation;
            }
        }
        if (!TryAuthorizeQueueRetry(view, operation, out var principal, preserveBudgetFailure) || principal is null)
        {
            return operation;
        }
        var scope = CommandOutcomePartitionIdentity.Resolve(operation);
        var selection = CommandOutcomeKeyResolver.Select(view, operation.UserId!, operation.CommandId, scope);
        if (selection.Outcome is { } outcome)
        {
            ValidateRetainedQueueRetryAdmission(view, principal, operation, selection, outcome);
            return operation;
        }
        List<QueueRetryInput> inputs;
        try
        {
            inputs = ReadQueueRetryInputs(view, principal, operation);
        }
        catch (KeyLoadException failure) when (failure.Code is not (ErrorCode.Corruption or ErrorCode.FormatUnsupported
            or ErrorCode.RecoveryRequired or ErrorCode.UnknownWriteOutcome)
            && (!preserveBudgetFailure || failure.Code != ErrorCode.BudgetExceeded))
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
            var delay = ChooseQueueRetryDelay(operation, input.Lane, input.Policy, input.Metadata);
            choices.Add(new(input.Lane, input.Metadata.Id, input.Metadata.StateVersion, input.Metadata.LeaseVersion,
                input.Metadata.DeliveryGeneration, input.Metadata.Attempts, input.DueIndexKey,
                SHA256.HashData(OrleansSerialization.Serialize(input.Policy)), delay, operation.Timestamp.AddMilliseconds(delay)));
        }
        var decisions = OrleansSerialization.Serialize(new QueueRetryDecisions(QueueRetryProtocol.Version,
            operation.Timestamp, choices.MoveToImmutable()));
        RequireBudget(decisions.Length);
        var payload = operation.Value!;
        var prepared = OwnCommand(operation);
        prepared.Value = payload with { RetryDecisions = decisions };
        return prepared;
    }
}
