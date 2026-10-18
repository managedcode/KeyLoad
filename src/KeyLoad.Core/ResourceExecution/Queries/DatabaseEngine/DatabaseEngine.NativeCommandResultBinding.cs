using System.Collections.Immutable;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal static bool IsResourceFreeCommandWork(OperationKind kind) => IsResourceFreeNativeOperation(kind);

    internal void BindAuthenticatedCommandResultLifetimes(string verifiedPrincipalId, long expectedPolicyEpoch,
        ImmutableArray<ResourceLifetimeV1> lifetimes, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        work.AttachResourceLifetimeBinding(binding);
        if (lifetimes.IsDefault || lifetimes.Length > Limits.MaxScanRecords)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceEffectAdmissionProtocol.Invalid);
        }
        work.Check();
        _ = Store.Read(view =>
        {
            var charged = work.CreateView(view);
            var principal = Principal(charged, verifiedPrincipalId, EvaluationClock.GetUtcNow());
            if (principal.PolicyEpoch != expectedPolicyEpoch)
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, ChangedOutcomePrincipalPolicyMessage);
            }
            foreach (var lifetime in lifetimes)
            {
                ResourceOwnerInventoryValidation.Lifetime(lifetime);
            }
            work.ObserveAuthenticatedResourceLifetimes(lifetimes);
            return true;
        });
        work.Check();
    }
}
