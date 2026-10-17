using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private StoredOutcome? ReadRemoteGraphOriginalOutcome(IKeyValueView view, PrincipalRecord principal,
        RemoteGraphSourceReadRequestV1 request)
    {
        var scope = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, request.SourcePartition);
        var selected = CommandOutcomeKeyResolver.Select(view, principal.Id, request.OriginalCommandId, scope);
        if (selected.Outcome is not { } outcome)
        {
            return null;
        }
        if (outcome.PolicyEpoch != principal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ChangedOutcomePrincipalPolicyMessage);
        }
        if (outcome.Incarnation != Store.Identity.Incarnation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, EarlierOutcomeIncarnationMessage);
        }
        if (outcome.Result is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
        }
        if (outcome.Result.Error is not null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteGraphProtocol.Invalid);
        }
        var retained = outcome.Result.Get<RemoteGraphSourceResultV1>();
        if (retained is null || retained.Receipt is null
            || retained.Original is null || retained.Original.Original is null
            || retained.Receipt.CommandId != request.OriginalCommandId)
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
        }
        var expected = Identity<RemoteGraphSourceIdentityV1>(NativeSerialization.Serialize(request.Original));
        var actual = Identity<RemoteGraphSourceIdentityV1>(NativeSerialization.Serialize(retained.Original.Original));
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Conflict, RemoteGraphProtocol.Invalid);
        }
        _ = RequireRemoteGraphSource(view, principal, retained.Original);
        ValidateRemoteGraphRetainedSourceResult(view, principal, request, retained);
        var payload = NativeSerialization.Serialize(retained.Original);
        RequireNativeBudget(payload.Length);
        var operation = IssueNativeOperation(new(request.OriginalCommandId, OperationKind.RemoteGraphSource,
            principal.Id, EvaluationClock.GetUtcNow(), expected), new Features.InternalSerialization.NativeCommandPayload(payload));
        ValidateSubmissionOutcomeIdentity(view, principal, operation, selected, outcome);
        ValidateCachedResult(view, principal, operation, outcome);
        return outcome;
    }
}
