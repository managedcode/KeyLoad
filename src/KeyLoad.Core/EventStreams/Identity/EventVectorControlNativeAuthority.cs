using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ReplicatedOperation CreateVerifiedEventVectorControlPhase(string localPrincipalId,
        EventVectorControlPhase verifiedPhase, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(verifiedPhase);
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        var now = EvaluationClock.GetUtcNow();
        var admission = new EventVectorAdmissionPolicy(OperationLimitsOptions);
        Store.Read(view =>
        {
            var bounded = work.CreateView(view);
            var principal = Principal(bounded, localPrincipalId, now);
            _ = RequireEventVectorControlView(bounded, principal, verifiedPhase);
            return true;
        });
        var (id, metadata) = EventVectorControlOperationIdentity.Create(verifiedPhase, localPrincipalId, admission);
        RequireNativeBudget(System.Text.Encoding.UTF8.GetByteCount(metadata));
        admission.RequireEncodedBytes(NativeSerialization.Measure(verifiedPhase));
        var body = NativeSerialization.Serialize(verifiedPhase);
        admission.RequireEncodedBytes(body.Length);
        work.Check();
        var operation = IssueNativeOperation(new(id, OperationKind.EventFeedControl, localPrincipalId, now, metadata),
            new NativeCommandPayload(body));
        work.Check();
        return operation;
    }
}
