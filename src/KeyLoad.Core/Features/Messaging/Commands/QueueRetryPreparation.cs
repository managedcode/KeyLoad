using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ReplicatedOperation PrepareQueueRetryOperation(ReplicatedOperation operation)
        => Store.Read(view => PrepareQueueRetryInView(view, operation));

    private ReplicatedOperation PrepareQueueRetryInView(IKeyValueView view, ReplicatedOperation operation)
    {
        if (operation.Kind is not (OperationKind.Delivery or OperationKind.Receive))
        { return operation; }
        var original = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);
        if (!original.RetryDecisions.IsEmpty)
        { return operation; }
        var scope = CommandOutcomePartitionIdentity.Resolve(operation);
        if (CommandOutcomeKeyResolver.Select(view, operation.PrincipalId, operation.Id, scope).Outcome is not null)
        { return operation; }
        List<QueueRetryInput> inputs;
        try
        {
            var principal = Principal(view, operation.PrincipalId, operation.EvaluatedAt);
            _ = AuthorizeOperation(view, principal, operation);
            inputs = ReadQueueRetryInputs(view, principal, operation);
        }
        catch (KeyLoadException failure) when (failure.Code is not (ErrorCode.Corruption or ErrorCode.FormatUnsupported
            or ErrorCode.RecoveryRequired or ErrorCode.UnknownWriteOutcome))
        { return operation; }
        if (inputs.Count == QueueRetryProtocol.Initial)
        { return operation; }
        var choices = ImmutableArray.CreateBuilder<QueueRetryDecision>(inputs.Count);
        foreach (var input in inputs)
        {
            var delay = ChooseQueueRetryDelay(operation, input.Lane, input.Policy, input.Metadata);
            choices.Add(new(input.Lane, input.Metadata.Id, input.Metadata.StateVersion, input.Metadata.LeaseVersion,
                input.Metadata.DeliveryGeneration, input.Metadata.Attempts, input.DueIndexKey,
                SHA256.HashData(NativeSerialization.Serialize(input.Policy)), delay, operation.EvaluatedAt.AddMilliseconds(delay)));
        }
        var decisions = NativeSerialization.Serialize(new QueueRetryDecisions(QueueRetryProtocol.Version,
            operation.EvaluatedAt, choices.MoveToImmutable()));
        RequireNativeBudget(decisions.Length);
        var payload = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);
        return IssueNativeOperation(operation, payload with { RetryDecisions = decisions });
    }
}
