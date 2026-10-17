using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal static class ReplicaPrefixGcTrialCleanup
{
    internal static async Task DeleteAsync(string root, List<Exception> failures, CancellationToken cancellationToken)
    {
        foreach (var nodeRoot in new[]
        {
            root,
            Path.Combine(root, ReplicaPrefixGcFreshTarget.DirectoryName),
            Path.Combine(root, ReplicaPrefixGcBoundaryAssertions.DirectoryName)
        })
        {
            await ServerFailureObserver.ObserveAsync(
                () => ReplicaProcessFiles.WaitForOwnershipAsync(nodeRoot, cancellationToken), failures).ConfigureAwait(false);
        }
        if (failures.Count == 0)
        {
            await ServerFailureObserver.ObserveAsync(
                () => ReplicaProcessFiles.DeleteAsync(root, cancellationToken), failures).ConfigureAwait(false);
        }
    }
}
