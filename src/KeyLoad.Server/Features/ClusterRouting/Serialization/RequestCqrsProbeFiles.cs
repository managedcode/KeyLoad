using System.Security.Cryptography;
using KeyLoad.Replication;
using KeyLoad.Storage.IO;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsProbeFiles
{
    private const string NameSeparator = "-";
    private const string TemporaryFileSuffix = ".tmp";

    private readonly IOptions<RequestProbeExecutionOptions> executionOptions;
    private readonly RequestCqrsProbeJson json;
    private readonly Lock sync = new();
    private readonly string root;
    internal string SessionId { get; }
    private readonly byte[] ownerBytes;
    private readonly RequestCqrsProbeRecords records;
    private readonly RequestCqrsProbeActivationInventory activations;
    internal readonly RequestCqrsProbeLiveFiles Live;

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
        Live = new(root, sessionId, json, records, executionOptions, sync, ReadSnapshotLocked, WriteAtomic);
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

    private RequestCqrsProbeSnapshot ReadSnapshotLocked()
    {
        const int MaximumFilesStep = 1;
        const int AggregateBytesInitialValue = 0;

        RequestCqrsProbePaths.RequireDirectory(root);
        var currentOwner = ReadRecord(Path.Combine(root, RequestCqrsProbeProtocol.OwnerFile), executionOptions);
        if (!CryptographicOperations.FixedTimeEquals(ownerBytes, currentOwner))
        { throw Invalid(); }
        var entries = Directory.EnumerateFileSystemEntries(root).Take(executionOptions.Value.MaximumFiles + MaximumFilesStep).ToArray();
        if (entries.Length > executionOptions.Value.MaximumFiles)
        { throw Invalid(); }
        var arms = new List<RequestCqrsProbeLoadedArm>();
        var releases = new List<RequestCqrsProbeReleaseRecord>();
        var markers = new List<RequestCqrsProbeMarkerRecord>();
        var witnesses = new List<RequestCqrsProbeActivationRecord>();
        var live = new List<RequestCqrsProbeLiveRecord>();
        var presentControls = new HashSet<string>(StringComparer.Ordinal);
        long aggregateBytes = AggregateBytesInitialValue;
        foreach (var path in entries)
        {
            var name = Path.GetFileName(path);
            aggregateBytes = checked(aggregateBytes + ValidateEntry(path, name));
            if (aggregateBytes > executionOptions.Value.MaximumAggregateBytes)
            { throw Invalid(); }
            if (RequestCqrsProbeLiveValidation.IsName(name))
            { Live.Read(path, name, live, presentControls); }
            else if (RequestCqrsProbeActivationValidation.IsName(name))
            { activations.Read(path, name, witnesses, presentControls); }
            else
            { records.ReadControl(path, name, arms, releases, markers, presentControls); }
        }
        RequestCqrsProbeActivationInventory.RequireMarkers(witnesses, markers);
        RequestCqrsProbeLiveFiles.RequireInventory(live, witnesses, markers, root, executionOptions);
        records.ValidatePresence(presentControls);
        records.ValidateInventory(markers);
        records.ValidateCrossRecords(arms, releases, markers);
        records.CommitArmInventory(arms, releases, markers);
        return new(arms, releases, markers, entries.Length, aggregateBytes);
    }

    private long ValidateEntry(string path, string name)
    {
        var identity = OfflineRegularFile.Inspect(path);
        RequestCqrsProbePaths.RequirePrivateMode(path, RequestCqrsProbeProtocol.PrivateFileMode);
        var length = identity.Length;
        if (length > executionOptions.Value.MaximumRecordBytes || !KnownName(name))
        { throw Invalid(); }
        return length;
    }

    private void WriteAtomic(string destination, byte[] bytes)
        => RequestCqrsProbeAtomicFiles.Write(root, destination, bytes, executionOptions, ReadSnapshotLocked);

    internal static byte[] ReadRecord(string path, IOptions<RequestProbeExecutionOptions> executionOptions)
        => RequestCqrsProbeFileReader.Read(path, executionOptions);

    private static bool KnownName(string name) => RequestCqrsProbeLiveValidation.IsName(name)
        || RequestCqrsProbeActivationValidation.IsName(name)
        || name == RequestCqrsProbeProtocol.OwnerFile
        || IsGuidName(name, RequestCqrsProbeProtocol.ArmFilePrefix, RequestCqrsProbeProtocol.JsonFileSuffix) || IsGuidName(name, RequestCqrsProbeProtocol.TemporaryFilePrefix, TemporaryFileSuffix)
        || name.StartsWith(RequestCqrsProbeProtocol.ReleaseFilePrefix, StringComparison.Ordinal) && name.EndsWith(RequestCqrsProbeProtocol.JsonFileSuffix, StringComparison.Ordinal)
        || name.StartsWith(RequestCqrsProbeProtocol.MarkerFilePrefix, StringComparison.Ordinal) && name.EndsWith(RequestCqrsProbeProtocol.JsonFileSuffix, StringComparison.Ordinal);

    private static bool IsGuidName(string name, string prefix, string suffix)
    {
        const string CompactIdentityFormat = "N";
        const int CompactGuidCharacterCount = 32;

        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal)
            || !Guid.TryParseExact(name.AsSpan(prefix.Length, name.Length - prefix.Length - suffix.Length), CompactIdentityFormat, out var value))
        { return false; }
        return name.AsSpan(prefix.Length, CompactGuidCharacterCount).SequenceEqual(value.ToString(RequestCqrsProbeProtocol.SessionIdFormat));
    }

    internal static string ArmName(RequestCqrsProbeArmRecord arm) => RequestCqrsProbeProtocol.ArmFilePrefix + arm.ArmId.ToString(RequestCqrsProbeProtocol.SessionIdFormat) + RequestCqrsProbeProtocol.JsonFileSuffix;
    internal static string ReleaseName(RequestCqrsProbeReleaseRecord release)
        => RequestCqrsProbeProtocol.ReleaseFilePrefix + release.ArmId.ToString(RequestCqrsProbeProtocol.SessionIdFormat) + NameSeparator + release.RequestId.ToString(RequestCqrsProbeProtocol.SessionIdFormat) + RequestCqrsProbeProtocol.JsonFileSuffix;
    internal static string MarkerName(RequestCqrsProbeMarkerRecord marker)
        => string.Concat(RequestCqrsProbeProtocol.MarkerFilePrefix, marker.ArmId.ToString(RequestCqrsProbeProtocol.SessionIdFormat), NameSeparator, marker.RequestId.ToString(RequestCqrsProbeProtocol.SessionIdFormat), NameSeparator, marker.Phase.ToString(), NameSeparator, marker.Outcome.ToString(), RequestCqrsProbeProtocol.JsonFileSuffix);
    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
