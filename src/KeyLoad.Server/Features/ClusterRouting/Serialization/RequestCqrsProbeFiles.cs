using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using KeyLoad.Replication;
using KeyLoad.Storage.IO;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsProbeFiles
{
    private const string KnownNamePrefixText = "arm-";
    private const string KnownNameJsonFileExtension = ".json";
    private const string KnownNameKnownNamePrefixText = "tmp-";
    private const string KnownNameTemporaryFileExtension = ".tmp";
    private const int DiscoveryNameSlotEmptyCount = 0;
    private const int DiscoveryNameSlotFirstCount = 1;

    private readonly IOptions<RequestProbeExecutionOptions> executionOptions;
    private readonly RequestCqrsProbeJson json;
    private readonly Lock sync = new();
    private readonly string root;
    internal string SessionId { get; }
    private readonly byte[] ownerBytes;
    private readonly RequestCqrsProbeRecords records;
    private readonly bool captureDiscovery;

    private RequestCqrsProbeFiles(string root, string sessionId, string voter, byte[] ownerBytes,
        IOptions<ReplicaConfiguration> replicaOptions, bool captureDiscovery, IOptions<RequestProbeExecutionOptions> executionOptions, RequestCqrsProbeJson json)
    {
        this.executionOptions = executionOptions;
        this.json = json;
        this.root = root;
        SessionId = sessionId;
        this.ownerBytes = ownerBytes;
        this.captureDiscovery = captureDiscovery;
        var discoveryPolicy = new RequestCqrsProbeDiscoveryPolicy(replicaOptions.Value.VoterIds, voter, captureDiscovery);
        records = new RequestCqrsProbeRecords(sessionId, voter, ownerBytes, discoveryPolicy, executionOptions, json);
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
        var files = new RequestCqrsProbeFiles(root, options.SessionId, voter, ownerBytes.ToArray(), replicaOptions,
            options.DiscoveryCaptureMode == RequestCqrsProbeProtocol.MixedInterface3Capture, executionOptions, json);
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

    internal void WriteDiscovery(RequestCqrsProbeDiscoveryRecord discovery)
    {
        lock (sync)
        {
            if (!captureDiscovery)
            { throw Invalid(); }
            var snapshot = ReadSnapshotLocked();
            records.ValidateDiscoveryForWrite(discovery);
            if (snapshot.Discoveries.Any(existing => existing.PeerVoterId == discovery.PeerVoterId))
            { return; }
            var slot = records.GetDiscoverySlot(discovery.PeerVoterId);
            WriteAtomic(Path.Combine(root, DiscoveryName(slot)), json.WriteDiscovery(discovery));
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
        var discoveries = new List<RequestCqrsProbeDiscoveryRecord>();
        var presentControls = new HashSet<string>(StringComparer.Ordinal);
        long aggregateBytes = AggregateBytesInitialValue;
        foreach (var path in entries)
        {
            var name = Path.GetFileName(path);
            aggregateBytes = checked(aggregateBytes + ValidateEntry(path, name));
            if (aggregateBytes > executionOptions.Value.MaximumAggregateBytes)
            { throw Invalid(); }
            records.ReadControl(path, name, arms, releases, markers, presentControls, discoveries);
        }
        records.ValidatePresence(presentControls);
        records.ValidateInventory(markers);
        records.ValidateDiscoveryInventory(discoveries);
        records.ValidateCrossRecords(arms, releases, markers);
        records.CommitArmInventory(arms, releases, markers);
        return new(arms, releases, markers, discoveries, entries.Length, aggregateBytes);
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
    {
        if (OperatingSystem.IsWindows())
        { throw Invalid(); }
        if (bytes.Length > executionOptions.Value.MaximumRecordBytes || File.Exists(destination))
        { throw Invalid(); }
        var before = ReadSnapshotLocked();
        if (before.FileCount >= executionOptions.Value.MaximumFiles
            || before.AggregateBytes + bytes.Length > executionOptions.Value.MaximumAggregateBytes)
        { throw Invalid(); }
        var temporary = Path.Combine(root, $"tmp-{Guid.NewGuid():N}.tmp");
        using (var stream = new FileStream(temporary, new FileStreamOptions
        {
            BufferSize = executionOptions.Value.FileBufferBytes,
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = RequestCqrsProbeProtocol.PrivateFileMode,
            Options = FileOptions.WriteThrough
        }))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        var staged = ReadSnapshotLocked();
        if (staged.FileCount > executionOptions.Value.MaximumFiles
            || staged.AggregateBytes > executionOptions.Value.MaximumAggregateBytes)
        { throw Invalid(); }
        File.Move(temporary, destination, overwrite: false);
    }

    internal static byte[] ReadRecord(string path, IOptions<RequestProbeExecutionOptions> executionOptions)
    {
        const int ReadInitialValue = 0;
        const int EmptyCount = 0;
        const int StartEmptyCount = 0;

        _ = OfflineRegularFile.Inspect(path);
        RequestCqrsProbePaths.RequirePrivateMode(path, RequestCqrsProbeProtocol.PrivateFileMode);
        using var stream = OfflineRegularFile.Open(path, FileAccess.Read, FileShare.Read, executionOptions.Value.ReadBufferBytes);
        var bytes = new byte[executionOptions.Value.ReadBufferBytes];
        var read = ReadInitialValue;
        while (read < bytes.Length)
        {
            var count = stream.Read(bytes, read, bytes.Length - read);
            if (count == EmptyCount)
            { break; }
            read += count;
        }
        if (read == bytes.Length)
        { throw Invalid(); }
        return bytes.AsSpan(StartEmptyCount, read).ToArray();
    }

    private static bool KnownName(string name) => name == RequestCqrsProbeProtocol.OwnerFile
        || IsGuidName(name, KnownNamePrefixText, KnownNameJsonFileExtension) || IsGuidName(name, KnownNameKnownNamePrefixText, KnownNameTemporaryFileExtension)
        || name.StartsWith(RequestCqrsProbeProtocol.ReleaseFilePrefix, StringComparison.Ordinal) && name.EndsWith(RequestCqrsProbeProtocol.JsonFileSuffix, StringComparison.Ordinal)
        || name.StartsWith(RequestCqrsProbeProtocol.MarkerFilePrefix, StringComparison.Ordinal) && name.EndsWith(RequestCqrsProbeProtocol.JsonFileSuffix, StringComparison.Ordinal)
        || name is RequestCqrsProbeProtocol.DiscoveryFileZero or RequestCqrsProbeProtocol.DiscoveryFileOne;

    private static bool IsGuidName(string name, string prefix, string suffix)
    {
        const string CompactIdentityFormat = "N";
        const int CompactGuidCharacterCount = 32;

        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal)
            || !Guid.TryParseExact(name.AsSpan(prefix.Length, name.Length - prefix.Length - suffix.Length), CompactIdentityFormat, out var value))
        { return false; }
        return name.AsSpan(prefix.Length, CompactGuidCharacterCount).SequenceEqual(value.ToString(RequestCqrsProbeProtocol.SessionIdFormat));
    }

    internal static string ArmName(RequestCqrsProbeArmRecord arm) => $"arm-{arm.ArmId:N}.json";
    internal static string ReleaseName(RequestCqrsProbeReleaseRecord release)
        => $"release-{release.ArmId:N}-{release.RequestId:N}.json";
    internal static string DiscoveryName(int slot)
        => slot is DiscoveryNameSlotEmptyCount or DiscoveryNameSlotFirstCount ? $"discovery-{slot:D2}.json" : throw Invalid();
    internal static string MarkerName(RequestCqrsProbeMarkerRecord marker)
        => $"marker-{marker.ArmId:N}-{marker.RequestId:N}-{marker.Phase}-{marker.Outcome}.json";
    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
