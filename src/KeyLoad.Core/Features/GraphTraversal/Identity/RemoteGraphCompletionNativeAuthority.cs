using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ReplicatedOperation CreateVerifiedRemoteGraphComplete(string principalId,
        RemoteGraphCompletionMutationV1 original, DateTimeOffset expiresAt, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        if (original.Version != RemoteGraphProtocol.Version || original.CommandId == Guid.Empty
            || expiresAt.Offset != TimeSpan.Zero || expiresAt <= EvaluationClock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteGraphProtocol.Invalid);
        }
        var verified = Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            var principal = Principal(view, principalId, EvaluationClock.GetUtcNow());
            _ = RequireRemoteGraphCompletion(view, principal, original.Receipt);
            var claims = new RemoteGraphCompletionClaimsV1(RemoteGraphProtocol.CompletionPurpose,
                original.Receipt, principal.Id, principal.PolicyEpoch, expiresAt);
            return original with
            {
                VerifiedToken = Sign(claims)
            };
        });
        var bytes = NativeSerialization.Serialize(verified);
        RequireNativeBudget(bytes.Length);
        var identity = Identity<RemoteGraphCompletionIdentityV1>(NativeSerialization.Serialize(
            new RemoteGraphCompletionIdentityV1(verified.CommandId, verified.Receipt)));
        work.Check();
        return IssueNativeOperation(new(verified.CommandId, OperationKind.RemoteGraphComplete,
            principalId, EvaluationClock.GetUtcNow(), identity), new Features.InternalSerialization.NativeCommandPayload(bytes));
    }
}
