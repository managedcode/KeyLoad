using System.Security.Cryptography;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsProbeFiles
{
    private const string NameSeparator = "-";

    private readonly IOptions<RequestProbeExecutionOptions> executionOptions;
    private readonly RequestCqrsProbeJson json;
    private readonly Lock sync = new();
    private readonly string root;
    internal string SessionId { get; }
    private readonly byte[] ownerBytes;
    private readonly RequestCqrsProbeRecords records;
    private readonly RequestCqrsProbeActivationInventory activations;
    private readonly ConnectionProbeFilePublication connectionFiles;
    internal readonly RequestCqrsReplicaSnapshotFiles ReplicaSnapshot;
    internal readonly RequestCqrsEventVectorExpiryFiles EventVectorExpiry;
    internal readonly RequestCqrsProbeLiveFiles Live;
    internal readonly RequestCqrsProbeMigrationFiles Migration;
    internal readonly RequestCqrsNativeModelFiles NativeModel;
    internal readonly RequestCqrsNativeReplayFiles Replay;
    internal readonly RequestCqrsQueryReadCutFiles QueryCuts;
    internal readonly RequestCqrsWaitForIndexReadCutFiles WaitCuts;
    internal readonly RequestCqrsMovementJournalFiles MovementJournals;

    private RequestCqrsProbeFiles(string root, string sessionId, string voter, byte[] ownerBytes,
        IOptions<RequestProbeExecutionOptions> executionOptions, RequestCqrsProbeJson json)
    {
        this.executionOptions = executionOptions;
        this.json = json;
        this.root = root;
        SessionId = sessionId;
        this.ownerBytes = ownerBytes;
        records = new RequestCqrsProbeRecords(sessionId, voter, ownerBytes, executionOptions, json);
        activations = new(sessionId, json, records, executionOptions);
        connectionFiles = new(root, sessionId, executionOptions, records, sync, ReadSnapshotLocked, WriteAtomic);
        ReplicaSnapshot = new(root, sessionId, json, records, executionOptions, sync, ReadSnapshotLocked, WriteAtomic);
        EventVectorExpiry = new(root, json, records, executionOptions, sync, ReadSnapshotLocked, WriteAtomic);
        Live = new(root, sessionId, json, records, executionOptions, sync, ReadSnapshotLocked, WriteAtomic);
        Migration = new(root, sessionId, new(json, executionOptions), records, executionOptions, sync, ReadSnapshotLocked, WriteAtomic);
        NativeModel = new(root, sessionId, executionOptions, records, json, sync, ReadSnapshotLocked);
        Replay = new(root, sessionId, executionOptions, records, json, sync, ReadSnapshotLocked);
        QueryCuts = new(root, sessionId, executionOptions, records, json, sync, ReadSnapshotLocked);
        WaitCuts = new(root, sessionId, executionOptions, records, json, sync, ReadSnapshotLocked);
        MovementJournals = new(root, sessionId, executionOptions, records, json, sync, ReadSnapshotLocked);
    }

    internal static RequestCqrsProbeFiles Open(RequestCqrsProbeOptions options, IOptions<ReplicaConfiguration> replicaOptions, IOptions<RequestProbeExecutionOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        executionOptions.Value.Validate();
        var json = new RequestCqrsProbeJson(executionOptions);
        var root = RequestCqrsProbePaths.RequireRoot(options);
        RequestCqrsProbePaths.RequireDirectory(root);
        var voter = replicaOptions.Value.LocalId;
        var ownerBytes = ReadRecord(Path.Combine(root, RequestCqrsProbeProtocol.OwnerFile), executionOptions);
        var owner = json.ReadOwner(ownerBytes);
        if (owner.SessionId != options.SessionId || owner.Voter != voter)
        { throw Invalid(); }
        var files = new RequestCqrsProbeFiles(root, options.SessionId, voter, ownerBytes.ToArray(), executionOptions, json);
        _ = files.ReadSnapshot();
        return files;
    }

    internal RequestCqrsProbeSnapshot ReadSnapshot()
    {
        lock (sync)
        { return ReadSnapshotLocked(); }
    }

    internal void RequireActiveArm(RequestCqrsProbeLoadedArm arm)
        => RequireActiveArm(arm, ReadSnapshot());

    internal static void RequireActiveArm(RequestCqrsProbeLoadedArm arm, RequestCqrsProbeSnapshot snapshot)
    {
        var current = snapshot.Arms.SingleOrDefault(candidate => candidate.Record.ArmId == arm.Record.ArmId);
        if (current is null || !CryptographicOperations.FixedTimeEquals(arm.ExactBytes, current.ExactBytes))
        { throw Invalid(); }
    }

    internal void RequireClaimArmActiveOrRetired(RequestCqrsProbeLoadedArm arm)
    {
        lock (sync)
        {
            var current = ReadSnapshotLocked().Arms.SingleOrDefault(candidate => candidate.Record.ArmId == arm.Record.ArmId);
            if (current is not null && CryptographicOperations.FixedTimeEquals(arm.ExactBytes, current.ExactBytes))
            { return; }
            if (current is null && records.IsRetiredArm(arm.Record.ArmId, arm.ExactBytes))
            { return; }
            throw Invalid();
        }
    }

    internal void WriteMarker(RequestCqrsProbeMarkerRecord marker, RequestCqrsProbeLoadedArm? producerClaim = null)
    {
        lock (sync)
        {
            var snapshot = ReadSnapshotLocked();
            records.ValidateMarker(marker, snapshot, producerClaim);
            var name = MarkerName(marker);
            var bytes = json.WriteMarker(marker);
            WriteAtomic(Path.Combine(root, name), bytes);
            records.RegisterImmutable(name, bytes);
        }
    }

    internal void WriteNativeModel(RequestCqrsNativeModelWitness witness, RequestCqrsProbeMarkerRecord marker)
        => NativeModel.Publish(witness, marker);

    internal void WriteActivation(RequestCqrsProbeActivationRecord value)
    {
        lock (sync)
        {
            RequestCqrsProbeActivationValidation.RequireMarker(value, ReadSnapshotLocked().Markers);
            var name = RequestCqrsProbeActivationValidation.Name(value);
            var bytes = json.WriteActivation(value);
            WriteAtomic(Path.Combine(root, name), bytes);
            records.RegisterImmutable(name, bytes);
        }
    }

    internal void WriteConnection(ConnectionProbeWitness value)
        => connectionFiles.Write(value);

    private RequestCqrsProbeSnapshot ReadSnapshotLocked()
    {
        var entries = RequestCqrsProbeSnapshotFiles.RequireInventory(root, ownerBytes, executionOptions);
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
            if (RequestCqrsMovementJournalInventory.IsName(name))
            { MovementJournals.Read(path, name, movementJournals, presentControls); continue; }
            if (RequestCqrsWaitForIndexReadCutInventory.IsName(name))
            { WaitCuts.Read(path, name, waitCuts, presentControls); continue; }
            if (RequestCqrsQueryReadCutInventory.IsName(name))
            { QueryCuts.Read(path, name, queryCuts, presentControls); continue; }
            if (RequestCqrsNativeReplayInventory.IsPlanName(name) || RequestCqrsNativeReplayInventory.IsWitnessName(name))
            { Replay.Read(path, name, replayPlans, replayWitnesses, presentControls); continue; }
            if (RequestCqrsNativeModelInventory.IsName(name))
            { NativeModel.Read(path, name, nativeModels, presentControls); continue; }
            if (RequestCqrsEventVectorExpiryJson.IsName(name))
            { EventVectorExpiry.Read(path, name, eventVectorExpiries, presentControls); continue; }
            if (RequestCqrsReplicaSnapshotJson.IsName(name))
            { ReplicaSnapshot.Read(path, name, replicaSnapshots, presentControls); continue; }
            if (ConnectionProbeInventory.IsName(name))
            { connectionFiles.Read(path, name, connections, presentControls); continue; }
            if (RequestCqrsProbeMigrationValidation.IsName(name))
            { Migration.Read(path, name, migration, presentControls); continue; }
            if (RequestCqrsProbeLiveValidation.IsName(name))
            { Live.Read(path, name, live, presentControls); continue; }
            if (RequestCqrsProbeActivationValidation.IsName(name))
            { activations.Read(path, name, witnesses, presentControls); continue; }
            records.ReadControl(path, name, arms, releases, markers, presentControls);
        }
        RequestCqrsWaitForIndexReadCutInventory.RequireInventory(waitCuts, markers);
        MovementJournals.RequireInventory(movementJournals, markers, arms);
        WaitCuts.RequireArms(waitCuts, arms);
        RequestCqrsQueryReadCutInventory.RequireInventory(queryCuts, markers);
        QueryCuts.RequireArms(queryCuts, arms);
        RequestCqrsNativeModelInventory.RequireMarkers(nativeModels, markers);
        RequestCqrsNativeReplayInventory.RequireInventory(replayPlans, replayWitnesses, arms, markers, records.RetainedArm);
        RequestCqrsReplicaSnapshotFiles.RequireInventory(replicaSnapshots, arms, markers);
        RequestCqrsEventVectorExpiryFiles.RequireInventory(eventVectorExpiries, arms, markers);
        RequestCqrsProbeActivationInventory.RequireMarkers(witnesses, markers);
        ConnectionProbeInventory.RequireMarkers(connections, markers);
        RequestCqrsProbeMigrationFiles.RequireInventory(migration, witnesses, markers);
        RequestCqrsProbeLiveFiles.RequireInventory(live, witnesses, markers, root, executionOptions);
        ValidateRecordInventories(presentControls, markers, arms, releases, migration);
        return new(arms, releases, markers, entries.Length, aggregateBytes);
    }

    private void ValidateRecordInventories(HashSet<string> presentControls,
        List<RequestCqrsProbeMarkerRecord> markers, List<RequestCqrsProbeLoadedArm> arms,
        List<RequestCqrsProbeReleaseRecord> releases, List<RequestCqrsProbeMigrationRecord> migration)
    {
        records.ValidatePresence(presentControls);
        records.ValidateInventory(markers);
        records.ValidateCrossRecords(arms, releases, markers);
        records.CommitArmInventory(arms, releases, markers);
        Migration.CommitInventory(migration);
    }

    private long ValidateEntry(string path, string name)
        => RequestCqrsProbeSnapshotFiles.ValidateEntry(path, name, executionOptions);

    private void WriteAtomic(string destination, byte[] bytes)
        => RequestCqrsProbeAtomicFiles.Write(root, destination, bytes, executionOptions, ReadSnapshotLocked);

    internal void WriteClaimedProducerDisposed(RequestCqrsProbeMarkerRecord marker, RequestCqrsProbeLoadedArm claim)
        => new RequestCqrsProbeClaimedCleanup(root, ownerBytes, executionOptions, json, records, sync, ValidateEntry).Write(marker, claim);

    internal static byte[] ReadRecord(string path, IOptions<RequestProbeExecutionOptions> executionOptions)
        => RequestCqrsProbeFileReader.Read(path, executionOptions);

    internal static string ArmName(RequestCqrsProbeArmRecord arm) => RequestCqrsProbeProtocol.ArmFilePrefix + arm.ArmId.ToString(RequestCqrsProbeProtocol.SessionIdFormat) + RequestCqrsProbeProtocol.JsonFileSuffix;
    internal static string ReleaseName(RequestCqrsProbeReleaseRecord release)
        => RequestCqrsProbeProtocol.ReleaseFilePrefix + release.ArmId.ToString(RequestCqrsProbeProtocol.SessionIdFormat) + NameSeparator + release.RequestId.ToString(RequestCqrsProbeProtocol.SessionIdFormat) + RequestCqrsProbeProtocol.JsonFileSuffix;
    internal static string MarkerName(RequestCqrsProbeMarkerRecord marker)
        => string.Concat(RequestCqrsProbeProtocol.MarkerFilePrefix, marker.ArmId.ToString(RequestCqrsProbeProtocol.SessionIdFormat), NameSeparator, marker.RequestId.ToString(RequestCqrsProbeProtocol.SessionIdFormat), NameSeparator, marker.Phase.ToString(), NameSeparator, marker.Outcome.ToString(), RequestCqrsProbeProtocol.JsonFileSuffix);
    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
