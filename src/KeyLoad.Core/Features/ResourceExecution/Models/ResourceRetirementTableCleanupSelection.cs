using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal sealed record ResourceRetirementTableCleanupSelection(
    ResourceRetirementTableCleanupProgress Progress, IReadOnlyList<StorageMutation> Deletes)
{
    internal void Apply(IAtomicTransaction transaction)
    {
        foreach (var deletion in Deletes)
        {
            transaction.Delete(deletion.Key.ToArray());
        }
    }
}
