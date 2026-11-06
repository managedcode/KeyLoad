using System.Collections.Immutable;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes CommandResultBinding operations against the caller's database context.</summary>
public static class CommandResultBinding
{
    internal static bool IsResourceFreeCommandWork(OperationKind kind) => ResourceMetadataScopes.IsResourceFreeOperation(kind);
    internal static void BindAuthenticatedCommandResultLifetimes(this DatabaseEngine engine, long expectedPolicyEpoch,
        ImmutableArray<ResourceLifetime> lifetimes, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding,
        Func<IKeyValueView, PrincipalRecord> readPrincipal)
    {
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(readPrincipal) /* KLD0041: The BCL null guard accepts this existing principal-reader callback reference through its object parameter. */;
        work.AttachResourceLifetimeBinding(binding);
        if (lifetimes.IsDefault)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceEffectAdmissionProtocol.Invalid);
        }

        work.Check();
        _ = engine.Store.Read(view =>
        {
            var charged = work.CreateView(view);
            var principal = readPrincipal(charged);
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
