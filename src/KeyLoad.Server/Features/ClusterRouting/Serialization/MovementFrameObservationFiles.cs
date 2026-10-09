using System.Security.Cryptography;
using KeyLoad.Storage.IO;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class MovementFrameObservationFiles
{
    private const int InventoryOverflowStep = 1;
    private const long InitialAggregateBytes = 0;
    private const string TemporarySuffix = ".tmp";
    private readonly string root;
    private readonly string session;
    private readonly byte[] ownerBytes;
    private readonly IOptions<RequestProbeExecutionOptions> options;
    private readonly MovementFrameObservationJson json;
    private readonly Lock sync = new();
    private byte[]? selectedBytes;
    private byte[]? observedBytes;
    internal MovementFrameObservationFiles(MovementFrameObservationOptions enrollment, string voter,
        IOptions<RequestProbeExecutionOptions> options)
    {
        root = enrollment.Root ?? throw Invalid();
        session = enrollment.SessionId;
        this.options = options;
        json = new(options);
        RequestCqrsProbePaths.RequireDirectory(root, MovementFrameObservationProtocol.FixedRoot);
        ownerBytes = RequestCqrsProbeFiles.ReadRecord(Path.Combine(root, MovementFrameObservationProtocol.OwnerFile), options);
        var owner = json.ReadOwner(ownerBytes);
        if (!enrollment.Enabled || owner.SessionId != session || owner.Voter != voter)
        { throw Invalid(); }
        _ = ReadInventory();
    }

    internal MovementFrameObservationSelection? ReadSelection()
    {
        lock (sync)
        {
            _ = ReadInventory();
            var path = Path.Combine(root, MovementFrameObservationProtocol.SelectionFile);
            if (!File.Exists(path))
            {
                if (selectedBytes is not null)
                { throw Invalid(); }
                return null;
            }
            var bytes = RequestCqrsProbeFiles.ReadRecord(path, options);
            if (selectedBytes is not null && !CryptographicOperations.FixedTimeEquals(selectedBytes, bytes))
            { throw Invalid(); }
            var value = json.ReadSelection(bytes);
            if (value.SessionId != session)
            { throw Invalid(); }
            selectedBytes ??= bytes;
            return value;
        }
    }

    internal void Write(MovementFrameObservationRecord value)
    {
        lock (sync)
        {
            var selection = ReadSelection() ?? throw Invalid();
            if (value.SessionId != session || value.SelectionId != selection.SelectionId
                || value.MoveId != selection.MoveId || value.Partition != selection.Partition
                || value.OperatorPrincipalId != selection.OperatorPrincipalId
                || value.PhysicalShardId != selection.PhysicalShardId || value.Incarnation != selection.Incarnation)
            { throw Invalid(); }
            var bytes = json.WriteObservation(value);
            RequestCqrsProbeAtomicFiles.Write(root, Path.Combine(root, MovementFrameObservationProtocol.ObservationFile),
                bytes, options, ReadInventory);
            observedBytes = bytes;
        }
    }

    private RequestCqrsProbeSnapshot ReadInventory()
    {
        RequestCqrsProbePaths.RequireDirectory(root, MovementFrameObservationProtocol.FixedRoot);
        var currentOwner = RequestCqrsProbeFiles.ReadRecord(Path.Combine(root, MovementFrameObservationProtocol.OwnerFile), options);
        if (!CryptographicOperations.FixedTimeEquals(ownerBytes, currentOwner))
        { throw Invalid(); }
        var entries = Directory.EnumerateFileSystemEntries(root).Take(options.Value.MaximumFiles + InventoryOverflowStep).ToArray();
        if (entries.Length > options.Value.MaximumFiles)
        { throw Invalid(); }
        var bytes = InitialAggregateBytes;
        foreach (var path in entries)
        {
            var name = Path.GetFileName(path);
            if (!KnownName(name))
            { throw Invalid(); }
            if (name == MovementFrameObservationProtocol.ObservationFile)
            {
                var actual = RequestCqrsProbeFiles.ReadRecord(path, options);
                if (observedBytes is null || !CryptographicOperations.FixedTimeEquals(observedBytes, actual))
                { throw Invalid(); }
            }
            var identity = OfflineRegularFile.Inspect(path);
            RequestCqrsProbePaths.RequirePrivateMode(path, RequestCqrsProbeProtocol.PrivateFileMode);
            if (identity.Length > options.Value.MaximumRecordBytes)
            { throw Invalid(); }
            bytes = checked(bytes + identity.Length);
            if (bytes > options.Value.MaximumAggregateBytes)
            { throw Invalid(); }
        }
        return new([], [], [], entries.Length, bytes);
    }
    private static bool KnownName(string name) => name is MovementFrameObservationProtocol.OwnerFile
        or MovementFrameObservationProtocol.SelectionFile or MovementFrameObservationProtocol.ObservationFile
        || name.Length > RequestCqrsProbeProtocol.TemporaryFilePrefix.Length + TemporarySuffix.Length
            && name.StartsWith(RequestCqrsProbeProtocol.TemporaryFilePrefix, StringComparison.Ordinal)
            && name.EndsWith(TemporarySuffix, StringComparison.Ordinal)
            && Guid.TryParseExact(name.AsSpan(RequestCqrsProbeProtocol.TemporaryFilePrefix.Length,
                name.Length - RequestCqrsProbeProtocol.TemporaryFilePrefix.Length - TemporarySuffix.Length),
                RequestCqrsProbeProtocol.SessionIdFormat, out var identity) && identity != Guid.Empty;
    private static InvalidOperationException Invalid() => new(MovementFrameObservationProtocol.Invalid);
}
