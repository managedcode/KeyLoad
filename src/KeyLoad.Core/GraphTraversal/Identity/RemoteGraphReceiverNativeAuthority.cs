using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ReplicatedOperation CreateVerifiedRemoteGraphReceive(string principalId,
        RemoteGraphReceiverMutationV1 original, DateTimeOffset expiresAt, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        if (expiresAt.Offset != TimeSpan.Zero || expiresAt <= EvaluationClock.GetUtcNow()
            || original.Version != RemoteGraphProtocol.Version || original.CommandId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteGraphProtocol.Invalid);
        }
        var verified = Store.Read(view => SignRemoteGraphReceive(work.CreateView(view), principalId,
            original, expiresAt, work));
        var bytes = NativeSerialization.Serialize(verified);
        RequireNativeBudget(bytes.Length);
        var identity = Identity<RemoteGraphReceiverIdentityV1>(NativeSerialization.Serialize(
            new RemoteGraphReceiverIdentityV1(verified.CommandId, verified.SourceProof.Intent)));
        work.Check();
        return IssueNativeOperation(new(verified.CommandId, OperationKind.RemoteGraphReceive,
            principalId, EvaluationClock.GetUtcNow(), identity), new Features.InternalSerialization.NativeCommandPayload(bytes));
    }

    private RemoteGraphReceiverMutationV1 SignRemoteGraphReceive(IKeyValueView view, string principalId,
        RemoteGraphReceiverMutationV1 original, DateTimeOffset expiresAt, ReadExecutionBudget work)
    {
        var principal = Principal(view, principalId, EvaluationClock.GetUtcNow());
        _ = RequireRemoteGraphReceiver(view, principal, original.SourceProof, work);
        var claims = new RemoteGraphReceiverClaimsV1(RemoteGraphProtocol.ReceiverPurpose, original.SourceProof,
            principal.Id, principal.PolicyEpoch, expiresAt);
        return original with
        {
            VerifiedToken = Sign(claims)
        };
    }
}
