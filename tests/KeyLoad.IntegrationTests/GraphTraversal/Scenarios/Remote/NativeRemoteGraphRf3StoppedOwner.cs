using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.CrashHost.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class NativeRemoteGraphRf3StoppedOwner
{
    internal static NativeBlobWholeOwnerCut Read(TwoRf3MembershipWave wave, string root, string node,
        ReplicaSiloDiscovery owner, Guid canonicalId, PhysicalOwnerDirectoryV1 directory, CancellationToken token)
    {
        wave.AssertAllNodeLocksReleased();
        var tuple = Array.IndexOf(TwoRf3MembershipProtocol.Nodes, node) < 3 ? directory.ControlOwner
            : directory.Owners.Single(value => value.Owner.PhysicalShardId != directory.ControlOwner.PhysicalShardId).Owner;
        if (owner.Incarnation != tuple.Incarnation || !tuple.VoterIds.Contains(owner.VoterId, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, "The original graph native owner differs from its actual configured tuple.");
        }
        var nodeRoot = NativeBlobWholePaths.RequireNode(root, node);
        var failures = new List<Exception>();
        ZoneTreeStore? canonical = null;
        ZoneTreeStore? replica = null;
        NativeBlobWholeOwnerCut? result = null;
        ServerFailureObserver.Observe(() =>
        {
            var options = IntegrationExecutionOptions.StorageExecution();
            canonical = ZoneTreeExistingStore.Open(new(NativeBlobWholePaths.Canonical(nodeRoot))
            {
                Incarnation = owner.Incarnation
            }, canonicalId, options);
            var resolved = new ZoneTreeStoreOptions(NativeBlobWholePaths.Replica(nodeRoot))
            {
                Incarnation = owner.Incarnation
            }.ResolveExecutionOptions(options);
            var originalIdentity = ZoneTreeIdentityFile.Read(NativeBlobWholePaths.Identity(resolved.Directory),
                resolved.MaximumIdentityFileBytes, resolved.StreamBufferBytes);
            replica = ZoneTreeExistingStore.Open(resolved, originalIdentity.NodeId, options);
            if (replica.Identity.Incarnation != owner.Incarnation || replica.Identity.NodeId == canonicalId
                || !CryptographicOperations.FixedTimeEquals(replica.Identity.SigningKey.Span, canonical.Identity.SigningKey.Span))
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, "The stopped native graph journal and canonical authority disagree.");
            }
            result = ReadOwned(canonical, replica, owner, tuple, resolved.Directory, token);
        }, failures);
        if (replica is { } journal)
        {
            ServerFailureObserver.Observe(journal.Dispose, failures);
        }
        if (canonical is { } database)
        {
            ServerFailureObserver.Observe(database.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException("The complete stopped original graph cut is absent.");
    }

    private static NativeBlobWholeOwnerCut ReadOwned(ZoneTreeStore canonical, ZoneTreeStore replica,
        ReplicaSiloDiscovery owner, PhysicalShardRecord tuple, string replicaDirectory, CancellationToken token)
    {
        var budget = new C1SubmissionJournalInspectionReadBudget(token);
        var limits = IntegrationExecutionOptions.DatabaseLimits().Value;
        var rows = NativeBlobWholeRows.Read(canonical, limits.MaxScanRecords, budget, token);
        var journalRows = NativeBlobWholeRows.Read(replica, limits.MaxScanRecords, budget, token);
        var catalog = NativeSerialization.Deserialize<PhysicalShardCatalog>(NativeBlobWholeRows.Required(rows.Rows,
            PhysicalShardCatalogRecordSerialization.CatalogKey(), budget));
        if (catalog.DefaultShard.PhysicalShardId != tuple.PhysicalShardId || catalog.DefaultShard.Incarnation != tuple.Incarnation
            || !catalog.DefaultShard.VoterIds.SequenceEqual(tuple.VoterIds, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, "The stopped graph catalog does not identify the actual original physical owner.");
        }
        var applied = NativeSerialization.Deserialize<long>(NativeBlobWholeRows.Required(rows.Rows, KeySpace.AppliedBytes, budget));
        var failures = new List<Exception>();
        C1SubmissionJournalInspectionDatabase? verification = null;
        NativeBlobWholeOwnerCut? result = null;
        ServerFailureObserver.Observe(() =>
        {
            verification = C1SubmissionJournalInspectionDatabase.Open(canonical, budget, token);
            var configuration = new ReplicaConfiguration(owner.VoterId, [.. tuple.VoterIds], replicaDirectory, owner.Incarnation);
            configuration.Validate();
            var journal = NativeBlobWholeJournal.Read(journalRows.Rows, configuration, verification.Database, applied, budget, token);
            result = new(owner, canonical.Identity.NodeId, replica.Identity.NodeId, rows.Position,
                journalRows.Position, rows.Rows, journalRows.Rows, applied, journal.State, journal.Entries);
        }, failures);
        if (verification is { } borrowed)
        {
            ServerFailureObserver.Observe(borrowed.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException("The full original graph journal was not admitted.");
    }
}
