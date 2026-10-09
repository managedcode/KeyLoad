using Aspire.Hosting;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Joins the same original graph and readers before inspecting original native owner locks.</summary>
internal static class ClusterRestoreRf3Shutdown
{
    private const string NodeOwner = "node.owner.lock";
    private const string DatabaseDirectory = "database";
    private const string ReplicaDirectory = "replica";
    private const string OwnerFile = "owner.lock";

    internal static async Task JoinAsync(ClusterRestoreRf3Fixture fixture, DistributedApplication owned,
        ClusterRestoreRf3OperatorObservation? operatorReader, ClusterRestoreRf3StageObservation? heldReader,
        bool started, bool serversAdmitted)
    {
        var failures = new List<Exception>();
        using var deadline = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline, TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(() => owned.StopAsync(deadline.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        if (operatorReader is { } reader)
        { await ServerFailureObserver.ObserveAsync(reader.JoinAsync, failures).ConfigureAwait(false); }
        if (heldReader is { } held)
        { await ServerFailureObserver.ObserveAsync(held.JoinAsync, failures).ConfigureAwait(false); }
        if (started && serversAdmitted && failures.Count == 0)
        {
            foreach (var node in ClusterRestoreRf3Protocol.Nodes)
            {
                var directory = Path.Combine(fixture.DataRoot, node);
                ServerFailureObserver.Observe(() => NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(directory, NodeOwner)), failures);
                ServerFailureObserver.Observe(() => NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(directory, DatabaseDirectory, OwnerFile)), failures);
                ServerFailureObserver.Observe(() => NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(directory, ReplicaDirectory, OwnerFile)), failures);
            }
        }
        if (started && failures.Count == 0)
        { ClusterRestoreRf3OfflineLocks.Require(fixture, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
