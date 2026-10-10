using KeyLoad.Replication;

namespace KeyLoad.Orleans;

/// <summary>Obtains genuine current signed evidence before the unchanged startup cohort predicate.</summary>
internal static class ReplicaCohortStartupAcquisition
{
    internal static async Task<ReplicaCohortAdmissionStatus> AcquireAsync(
        ReplicaSiloDiscoveryClient client, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(client);
        try
        {
            await client.EnsureCompatibleCohortAsync(token).ConfigureAwait(false);
        }
        catch (KeyLoadException original) when (original.Code == ErrorCode.OwnershipLost
            && original.Message == ReplicaProtocol.NoLeader)
        {
            token.ThrowIfCancellationRequested();
        }
        token.ThrowIfCancellationRequested();
        return client.ObserveCompatibleCohort();
    }
}
