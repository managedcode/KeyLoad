using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal RemoteGraphReceiverResultV1 ReadRemoteGraphReceiverReceipt(string principalId,
        RemoteGraphReceiverReceiptReadRequestV1 request, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        if (request.Intent?.Content is null || request.OriginalReceiveCommandId == Guid.Empty)
        { throw Errors.Fail(ErrorCode.Validation, RemoteGraphProtocol.Invalid); }
        var result = Store.Read(view => CaptureRemoteGraphReceiverReceipt(work.CreateView(view), principalId, request));
        work.CheckResult(result);
        return result;
    }

    private RemoteGraphReceiverResultV1 CaptureRemoteGraphReceiverReceipt(IKeyValueView view, string principalId,
        RemoteGraphReceiverReceiptReadRequestV1 request)
    {
        var principal = Principal(view, principalId, EvaluationClock.GetUtcNow());
        var content = request.Intent.Content;
        Authorization.Require(principal, content.Destination.Partition, content.Graph, Capability.GraphWrite);
        _ = Resource(view, content.Destination.Partition, content.Graph, ResourceKind.Graph);
        if (!content.Deleted)
        { _ = VisibleVertex(view, principal, content.Destination); }
        var scope = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, content.Destination.Partition);
        var selected = CommandOutcomeKeyResolver.Select(view, principal.Id, request.OriginalReceiveCommandId, scope);
        var outcome = selected.Outcome ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteGraphProtocol.Invalid);
        if (outcome.PolicyEpoch != principal.PolicyEpoch)
        { throw Errors.Fail(ErrorCode.PermissionDenied, ChangedOutcomePrincipalPolicyMessage); }
        if (outcome.Incarnation != Store.Identity.Incarnation)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, EarlierOutcomeIncarnationMessage); }
        if (outcome.Result is null)
        { throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid); }
        if (outcome.Result.Error is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteGraphProtocol.Invalid); }
        var retained = outcome.Result.Get<RemoteGraphReceiverResultV1>();
        if (retained?.Receipt is null || retained.Original?.SourceProof?.Intent is null
            || retained.Original.CommandId != request.OriginalReceiveCommandId
            || retained.Receipt.TargetReceipt?.CommandId != request.OriginalReceiveCommandId
            || !NativeSerialization.Serialize(retained.Original.SourceProof.Intent).AsSpan().SequenceEqual(
                NativeSerialization.Serialize(request.Intent)))
        { throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid); }
        RequireRemoteGraphReceiptShape(retained.Receipt, ErrorCode.Corruption);
        if (!NativeSerialization.Serialize(retained.Receipt.Intent).AsSpan().SequenceEqual(
            NativeSerialization.Serialize(request.Intent)))
        { throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid); }
        _ = RequireRemoteGraphReceiver(view, principal, retained.Original.SourceProof);
        var identity = Identity<RemoteGraphReceiverIdentityV1>(NativeSerialization.Serialize(
            new RemoteGraphReceiverIdentityV1(request.OriginalReceiveCommandId, request.Intent)));
        var bytes = NativeSerialization.Serialize(retained.Original);
        RequireNativeBudget(bytes.Length);
        var operation = IssueNativeOperation(new(request.OriginalReceiveCommandId, OperationKind.RemoteGraphReceive,
            principal.Id, EvaluationClock.GetUtcNow(), identity), new Features.InternalSerialization.NativeCommandPayload(bytes));
        ValidateSubmissionOutcomeIdentity(view, principal, operation, selected, outcome);
        ValidateCachedResult(view, principal, operation, outcome);
        return retained;
    }
}
