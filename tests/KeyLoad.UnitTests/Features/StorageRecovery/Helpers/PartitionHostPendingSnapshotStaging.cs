using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Authorization;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionHostPendingSnapshotStaging
{
    internal static void Run(PartitionHostRecoveryFixture fixture, HostReplicaStores source, HostReplicaStores target, PendingPrincipal principal)
    {
        fixture.RecordTarget(target);
        source.Database.Bootstrap(new(PartitionStoreProtocol.AdministratorId, PartitionStoreProtocol.AdministratorTenant,
            [new(PartitionStoreProtocol.Wildcard, PartitionStoreProtocol.Wildcard, Capability.All)], [PartitionStoreProtocol.Wildcard])
        {
            ClusterAdministrator = true
        }, DatabaseEngine.Credential(PartitionStoreProtocol.AdministratorId, PartitionStoreProtocol.AdministratorId, fixture.Options.AdminKey));
        NativeJournalAuthorityTestBootstrap.Initialize(source.Database);
        source.Canonical.Commit((transaction, _) =>
        {
            var key = KeySpace.Principal(RuntimeJournalIdentity.ProtectedPrincipalId);
            if (principal == PendingPrincipal.Missing)
            {
                transaction.Delete(key);
            }
            else if (principal == PendingPrincipal.Modified)
            {
                var original = transaction.GetRecord<PrincipalRecord>(key)!;
                transaction.PutRecord(key, original with
                {
                    Revoked = true
                });
            }
            transaction.PutRecord(KeySpace.Applied.ToArray(), PartitionHostRecoveryFixture.SnapshotCut);
            return true;
        });
        source.Log.SaveTermAndVote(PartitionHostRecoveryFixture.SnapshotTerm, null);
        source.Log.Append([new(1, PartitionHostRecoveryFixture.SnapshotTerm, null), new(PartitionHostRecoveryFixture.SnapshotCut, PartitionHostRecoveryFixture.SnapshotTerm, null)]);
        source.Log.Commit(PartitionHostRecoveryFixture.SnapshotCut);
        fixture.RecordPending(source.Snapshots.Create(PartitionHostRecoveryFixture.SnapshotCut, PartitionHostRecoveryFixture.SnapshotTerm));
        var incoming = new ReplicaSnapshotStore(target.Canonical, target.Log, UnitExecutionOptions.ReplicaConfiguration(target.Configuration), UnitExecutionOptions.ReplicaExecution(), boundary =>
        {
            if (boundary == ReplicaCrashBoundary.SnapshotVerified)
            {
                throw new VerifiedSnapshotInterruption();
            }
        });
        var offset = incoming.Begin(fixture.Pending);
        while (offset < fixture.Pending.Length)
        {
            var bytes = source.Snapshots.ReadChunk(fixture.Pending.TransferId, offset, source.Configuration.SnapshotChunkBytes);
            offset = incoming.Append(fixture.Pending.TransferId, offset, bytes);
        }
        incoming.Complete(fixture.Pending.TransferId);
    }

}
