using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeSnapshotReader
{
    internal static RequestCqrsProbeSnapshot Read(string root, byte[] ownerBytes,
        IOptions<RequestProbeExecutionOptions> executionOptions, RequestCqrsProbeRecords records,
        RequestCqrsProbeActivationInventory activations, ConnectionProbeFilePublication connectionFiles,
        RequestCqrsReplicaSnapshotFiles replicaSnapshotFiles, RequestCqrsEventVectorExpiryFiles eventVectorExpiryFiles,
        RequestCqrsProbeLiveFiles liveFiles, RequestCqrsProbeMigrationFiles migrationFiles,
        RequestCqrsNativeModelFiles nativeModelFiles, RequestCqrsNativeReplayFiles replayFiles,
        RequestCqrsQueryReadCutFiles queryCutFiles, RequestCqrsWaitForIndexReadCutFiles waitCutFiles,
        RequestCqrsMovementJournalFiles movementJournalFiles, RequestCqrsControlledLifetimeFiles controlledLifetimeFiles)
    {
        var entries = RequestCqrsProbeSnapshotFiles.RequireInventory(root, ownerBytes, executionOptions);
        var controlledLifetime = controlledLifetimeFiles.Inventory();
        var arms = new List<RequestCqrsProbeLoadedArm>();
        var releases = new List<RequestCqrsProbeReleaseRecord>();
        var markers = new List<RequestCqrsProbeMarkerRecord>();
        var witnesses = new List<RequestCqrsProbeActivationRecord>();
        var live = new List<RequestCqrsProbeLiveRecord>();
        var connections = new List<ConnectionProbeWitness>();
        var migration = new List<RequestCqrsProbeMigrationRecord>();
        var replicaSnapshots = new List<RequestCqrsReplicaSnapshotRecord>();
        var eventVectorExpiries = new List<RequestCqrsEventVectorExpiryRecord>();
        var nativeModels = new List<RequestCqrsNativeModelWitness>();
        var queryCuts = new List<RequestCqrsQueryReadCutWitness>();
        var waitCuts = new List<RequestCqrsWaitForIndexReadCutWitness>();
        var movementJournals = new List<RequestCqrsMovementJournalRecord>();
        var replayPlans = new List<RequestCqrsNativeReplayPlan>();
        var replayWitnesses = new List<RequestCqrsNativeReplayWitness>();
        var presentControls = new HashSet<string>(StringComparer.Ordinal);
        var aggregateBytes = RequestCqrsProbeSnapshotFiles.EmptyByteCount;
        foreach (var path in entries)
        {
            var name = Path.GetFileName(path);
            aggregateBytes = RequestCqrsProbeSnapshotFiles.ChargeEntry(path, name, aggregateBytes, executionOptions);
            if (RequestCqrsControlledLifetimeNames.IsName(name))
            {
                controlledLifetime.Read(path, name, presentControls);
                continue;
            }
            if (ReadQueryEntry(path, name, movementJournalFiles, waitCutFiles, queryCutFiles, replayFiles, nativeModelFiles,
                movementJournals, waitCuts, queryCuts, replayPlans, replayWitnesses, nativeModels, presentControls))
            {
                continue;
            }
            if (ReadLifecycleEntry(path, name, eventVectorExpiryFiles, replicaSnapshotFiles, connectionFiles,
                migrationFiles, liveFiles, activations, eventVectorExpiries, replicaSnapshots, connections,
                migration, live, witnesses, presentControls))
            {
                continue;
            }
            records.ReadControl(path, name, arms, releases, markers, presentControls);
        }
        controlledLifetime.Require(arms, markers);
        ValidateDataInventories(records, queryCutFiles, waitCutFiles, movementJournalFiles, arms, markers,
            nativeModels, queryCuts, waitCuts, movementJournals, replayPlans, replayWitnesses);
        RequestCqrsReplicaSnapshotFiles.RequireInventory(replicaSnapshots, arms, markers);
        RequestCqrsEventVectorExpiryFiles.RequireInventory(eventVectorExpiries, arms, markers);
        RequestCqrsProbeActivationInventory.RequireMarkers(witnesses, markers);
        ConnectionProbeInventory.RequireMarkers(connections, markers);
        RequestCqrsProbeMigrationFiles.RequireInventory(migration, witnesses, markers);
        RequestCqrsProbeLiveFiles.RequireInventory(live, witnesses, markers, root, executionOptions);
        ValidateRecordInventories(records, migrationFiles, presentControls, markers, arms, releases, migration);
        return new(arms, releases, markers, entries.Length, aggregateBytes);
    }

    private static void ValidateDataInventories(RequestCqrsProbeRecords records,
        RequestCqrsQueryReadCutFiles queryCutFiles, RequestCqrsWaitForIndexReadCutFiles waitCutFiles,
        RequestCqrsMovementJournalFiles movementJournalFiles, List<RequestCqrsProbeLoadedArm> arms,
        List<RequestCqrsProbeMarkerRecord> markers, List<RequestCqrsNativeModelWitness> nativeModels,
        List<RequestCqrsQueryReadCutWitness> queryCuts, List<RequestCqrsWaitForIndexReadCutWitness> waitCuts,
        List<RequestCqrsMovementJournalRecord> movementJournals, List<RequestCqrsNativeReplayPlan> replayPlans,
        List<RequestCqrsNativeReplayWitness> replayWitnesses)
    {
        RequestCqrsWaitForIndexReadCutInventory.RequireInventory(waitCuts, markers);
        movementJournalFiles.RequireInventory(movementJournals, markers, arms);
        waitCutFiles.RequireArms(waitCuts, arms);
        RequestCqrsQueryReadCutInventory.RequireInventory(queryCuts, markers);
        queryCutFiles.RequireArms(queryCuts, arms);
        RequestCqrsNativeModelInventory.RequireMarkers(nativeModels, markers);
        RequestCqrsNativeReplayInventory.RequireInventory(replayPlans, replayWitnesses, arms, markers, records.RetainedArm);
    }

    private static bool ReadQueryEntry(string path, string name,
        RequestCqrsMovementJournalFiles movementJournalFiles, RequestCqrsWaitForIndexReadCutFiles waitCutFiles,
        RequestCqrsQueryReadCutFiles queryCutFiles, RequestCqrsNativeReplayFiles replayFiles, RequestCqrsNativeModelFiles nativeModelFiles,
        List<RequestCqrsMovementJournalRecord> movementJournals, List<RequestCqrsWaitForIndexReadCutWitness> waitCuts,
        List<RequestCqrsQueryReadCutWitness> queryCuts, List<RequestCqrsNativeReplayPlan> replayPlans,
        List<RequestCqrsNativeReplayWitness> replayWitnesses, List<RequestCqrsNativeModelWitness> nativeModels,
        HashSet<string> presentControls)
    {
        if (RequestCqrsMovementJournalInventory.IsName(name))
        {
            movementJournalFiles.Read(path, name, movementJournals, presentControls);
            return true;
        }
        if (RequestCqrsWaitForIndexReadCutInventory.IsName(name))
        {
            waitCutFiles.Read(path, name, waitCuts, presentControls);
            return true;
        }
        if (RequestCqrsQueryReadCutInventory.IsName(name))
        {
            queryCutFiles.Read(path, name, queryCuts, presentControls);
            return true;
        }
        if (RequestCqrsNativeReplayInventory.IsPlanName(name) || RequestCqrsNativeReplayInventory.IsWitnessName(name))
        {
            replayFiles.Read(path, name, replayPlans, replayWitnesses, presentControls);
            return true;
        }
        if (RequestCqrsNativeModelInventory.IsName(name))
        {
            nativeModelFiles.Read(path, name, nativeModels, presentControls);
            return true;
        }
        return false;
    }

    private static bool ReadLifecycleEntry(string path, string name,
        RequestCqrsEventVectorExpiryFiles eventVectorExpiryFiles, RequestCqrsReplicaSnapshotFiles replicaSnapshotFiles,
        ConnectionProbeFilePublication connectionFiles, RequestCqrsProbeMigrationFiles migrationFiles,
        RequestCqrsProbeLiveFiles liveFiles, RequestCqrsProbeActivationInventory activations,
        List<RequestCqrsEventVectorExpiryRecord> eventVectorExpiries,
        List<RequestCqrsReplicaSnapshotRecord> replicaSnapshots, List<ConnectionProbeWitness> connections,
        List<RequestCqrsProbeMigrationRecord> migration, List<RequestCqrsProbeLiveRecord> live,
        List<RequestCqrsProbeActivationRecord> witnesses, HashSet<string> presentControls)
    {
        if (RequestCqrsEventVectorExpiryJson.IsName(name))
        {
            eventVectorExpiryFiles.Read(path, name, eventVectorExpiries, presentControls);
            return true;
        }
        if (RequestCqrsReplicaSnapshotJson.IsName(name))
        {
            replicaSnapshotFiles.Read(path, name, replicaSnapshots, presentControls);
            return true;
        }
        if (ConnectionProbeInventory.IsName(name))
        {
            connectionFiles.Read(path, name, connections, presentControls);
            return true;
        }
        if (RequestCqrsProbeMigrationValidation.IsName(name))
        {
            migrationFiles.Read(path, name, migration, presentControls);
            return true;
        }
        if (RequestCqrsProbeLiveValidation.IsName(name))
        {
            liveFiles.Read(path, name, live, presentControls);
            return true;
        }
        if (RequestCqrsProbeActivationValidation.IsName(name))
        {
            activations.Read(path, name, witnesses, presentControls);
            return true;
        }
        return false;
    }

    private static void ValidateRecordInventories(RequestCqrsProbeRecords records,
        RequestCqrsProbeMigrationFiles migrationFiles, HashSet<string> presentControls,
        List<RequestCqrsProbeMarkerRecord> markers, List<RequestCqrsProbeLoadedArm> arms,
        List<RequestCqrsProbeReleaseRecord> releases, List<RequestCqrsProbeMigrationRecord> migration)
    {
        records.ValidatePresence(presentControls);
        records.ValidateInventory(markers);
        records.ValidateCrossRecords(arms, releases, markers);
        records.CommitArmInventory(arms, releases, markers);
        migrationFiles.CommitInventory(migration);
    }
}
