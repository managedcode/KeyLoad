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
    internal string SessionId
    {
        get;
    }
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
        {
            throw Invalid();
        }
        var files = new RequestCqrsProbeFiles(root, options.SessionId, voter, ownerBytes.ToArray(), executionOptions, json);
        _ = files.ReadSnapshot();
        return files;
    }

    internal RequestCqrsProbeSnapshot ReadSnapshot()
    {
        lock (sync)
        {
            return ReadSnapshotLocked();
        }
    }

    internal void RequireActiveArm(RequestCqrsProbeLoadedArm arm)
        => RequireActiveArm(arm, ReadSnapshot());

    internal static void RequireActiveArm(RequestCqrsProbeLoadedArm arm, RequestCqrsProbeSnapshot snapshot)
    {
        var current = snapshot.Arms.SingleOrDefault(candidate => candidate.Record.ArmId == arm.Record.ArmId);
        if (current is null || !CryptographicOperations.FixedTimeEquals(arm.ExactBytes, current.ExactBytes))
        {
            throw Invalid();
        }
    }

    internal void RequireClaimArmActiveOrRetired(RequestCqrsProbeLoadedArm arm)
    {
        lock (sync)
        {
            var current = ReadSnapshotLocked().Arms.SingleOrDefault(candidate => candidate.Record.ArmId == arm.Record.ArmId);
            if (current is not null && CryptographicOperations.FixedTimeEquals(arm.ExactBytes, current.ExactBytes))
            {
                return;
            }
            if (current is null && records.IsRetiredArm(arm.Record.ArmId, arm.ExactBytes))
            {
                return;
            }
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
        => RequestCqrsProbeSnapshotReader.Read(root, ownerBytes, executionOptions, records, activations,
            connectionFiles, ReplicaSnapshot, EventVectorExpiry, Live, Migration, NativeModel, Replay,
            QueryCuts, WaitCuts, MovementJournals);

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
