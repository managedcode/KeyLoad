using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Checks actual live retirement state under the caller's existing exclusive replacement ownership.</summary>
internal static class ResourceRetirementSnapshotReplacement
{
    internal static void Require(IAtomicStore store, DatabaseLimits limits, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(work);
        limits.Validate();
        work.Check();
        store.Read(raw =>
        {
            var view = work.CreateView(raw);
            ResourceRetirementStorage.RequireNoRetirement(view, limits);
            ResourceRetirementPreparationStorage.RequireNoPreparation(view, limits);
            ResourceRetirementPreparedOperationStorage.RequireNoReplacement(view, limits);
            return true;
        });
        work.Check();
    }
}
