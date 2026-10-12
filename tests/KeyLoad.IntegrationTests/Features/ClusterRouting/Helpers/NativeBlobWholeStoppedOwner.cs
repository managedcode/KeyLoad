using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.CrashHost.Features.ClusterReplication;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Server.Features.BlobStorage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeBlobWholeStoppedOwner
{
    internal static NativeBlobWholeOwnerCut Read(TwoRf3MembershipWave wave, string root, string node,
        ReplicaSiloDiscovery owner, Guid originalCanonicalNodeId, CancellationToken token)
    {
        wave.AssertAllNodeLocksReleased();
        var boundOwner = wave.nativeBlobWireControls?.Owner
            ?? throw new InvalidOperationException("The original native Blob owner was not retained.");
        var offset = Array.IndexOf(TwoRf3MembershipProtocol.Nodes, node) < 3 ? 0 : 3;
        var tuple = offset == 0 ? boundOwner.SourceOwner : boundOwner.DestinationOwner;
        var voters = tuple.VoterIds;
        if (owner.VoterId != "http://" + node + ":8080" || owner.Incarnation != tuple.Incarnation
            || owner.ClusterId != boundOwner.ClusterId || !owner.TransportReady)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, "The retained original Blob owner does not match its resource/configured tuple."); }
        var nodeRoot = NativeBlobWholePaths.RequireNode(root, node);
        var failures = new List<Exception>();
        ZoneTreeStore? canonical = null;
        ZoneTreeStore? replica = null;
        NativeBlobWholeOwnerCut? result = null;
        ServerFailureObserver.Observe(() =>
        {
            var options = IntegrationExecutionOptions.StorageExecution(wave.movementMaxFrameBytes is { } maximum
                ? new ZoneTreeStorageExecutionOptions { MaxFrameBytes = maximum } : null);
            var openedCanonical = ZoneTreeExistingStore.Open(new(NativeBlobWholePaths.Canonical(nodeRoot))
            { Incarnation = owner.Incarnation }, originalCanonicalNodeId, options);
            canonical = openedCanonical;
            var replicaOptions = new ZoneTreeStoreOptions(NativeBlobWholePaths.Replica(nodeRoot))
            { Incarnation = owner.Incarnation };
            var resolved = replicaOptions.ResolveExecutionOptions(options);
            var identity = ZoneTreeIdentityFile.Read(NativeBlobWholePaths.Identity(resolved.Directory),
                resolved.MaximumIdentityFileBytes, resolved.StreamBufferBytes);
            var openedReplica = ZoneTreeExistingStore.Open(resolved, identity.NodeId, options);
            replica = openedReplica;
            var actualReplica = openedReplica.Identity;
            if (actualReplica.Incarnation != owner.Incarnation || actualReplica.NodeId == originalCanonicalNodeId
                || !CryptographicOperations.FixedTimeEquals(actualReplica.SigningKey.Span, openedCanonical.Identity.SigningKey.Span))
            { throw Errors.Fail(ErrorCode.TokenInvalidated, "The stopped original Blob journal authority does not match its canonical owner."); }
            var budget = new C1SubmissionJournalInspectionReadBudget(token);
            var limits = new DatabaseLimits();
            limits.Validate();
            var rows = NativeBlobWholeRows.Read(openedCanonical, limits.MaxScanRecords, budget, token);
            var journalRows = NativeBlobWholeRows.Read(openedReplica, limits.MaxScanRecords, budget, token);
            var catalog = NativeSerialization.Deserialize<PhysicalShardCatalog>(NativeBlobWholeRows.Required(rows.Rows,
                PhysicalShardCatalogRecordSerialization.CatalogKey(), budget));
            if (!NativeBlobWireShape.Same(tuple, catalog.DefaultShard))
            { throw Errors.Fail(ErrorCode.TokenInvalidated, "The stopped native Blob catalog is not the original configured physical owner."); }
            var applied = NativeSerialization.Deserialize<long>(NativeBlobWholeRows.Required(rows.Rows, KeySpace.AppliedBytes, budget));
            using var inspection = C1SubmissionJournalInspectionDatabase.Open(openedCanonical, budget, token);
            var database = inspection.Database;
            var configuration = new ReplicaConfiguration(owner.VoterId, [.. voters], resolved.Directory, owner.Incarnation);
            configuration.Validate();
            var journal = NativeBlobWholeJournal.Read(journalRows.Rows, configuration, database, applied, budget, token);
            result = new(owner, originalCanonicalNodeId, actualReplica.NodeId, rows.Position,
                journalRows.Position, rows.Rows, journalRows.Rows, applied, journal.State, journal.Entries);
        }, failures);
        if (replica is { } journalOwner)
        { ServerFailureObserver.Observe(journalOwner.Dispose, failures); }
        if (canonical is { } databaseOwner)
        { ServerFailureObserver.Observe(databaseOwner.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException("The complete stopped original Blob cut is absent.");
    }
}
