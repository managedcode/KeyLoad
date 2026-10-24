using System.Collections.Immutable;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Core;
/// <summary>Executes CommandResultBinding operations against the caller's database context.</summary>
public static class CommandResultBinding
{
    internal static bool IsResourceFreeCommandWork(OperationKind kind) => ResourceMetadataScopes.IsResourceFreeOperation(kind);
    internal static void BindAuthenticatedCommandResultLifetimes(this DatabaseEngine engine, string verifiedPrincipalId, long expectedPolicyEpoch, ImmutableArray<ResourceLifetime> lifetimes, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        work.AttachResourceLifetimeBinding(binding);
        if (lifetimes.IsDefault || lifetimes.Length > engine.Limits.MaxScanRecords)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceEffectAdmissionProtocol.Invalid);
        }

        work.Check();
        _ = engine.Store.Read(view =>
        {
            var charged = work.CreateView(view);
            var principal = engine.Principal(charged, verifiedPrincipalId, engine.EvaluationClock.GetUtcNow());
            if (principal.PolicyEpoch != expectedPolicyEpoch)
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, CommandOutcomeOperations.ChangedOutcomePrincipalPolicyMessage);
            }

            foreach (var lifetime in lifetimes)
            {
                ResourceLifetimeIdentity.Validate(lifetime);
            }

            work.ObserveAuthenticatedResourceLifetimes(lifetimes);
            return true;
        });
        work.Check();
    }
}
