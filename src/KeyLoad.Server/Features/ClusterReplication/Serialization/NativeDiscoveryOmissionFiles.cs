using System.Security.Cryptography;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class NativeDiscoveryOmissionFiles
{
    private readonly Lock sync = new();
    private readonly string root;
    private readonly string session;
    private readonly IOptions<RequestProbeExecutionOptions> options;
    private readonly NativeDiscoveryOmissionJson json;
    private readonly Dictionary<string, byte[]> immutable = new(StringComparer.Ordinal);
    private readonly string[] voters;
    private readonly RequestCqrsProbeJson ownerJson;
    internal NativeDiscoveryOmissionFiles(IOptions<NodeOptions> node, IOptions<RequestProbeExecutionOptions> options)
    {
        this.options = options;
        root = node.Value.NativeDiscoveryOmission.Root ?? throw Invalid();
        session = node.Value.NativeDiscoveryOmission.SessionId;
        voters = [.. node.Value.Peers];
        json = new(options);
        ownerJson = new(options);
        _ = Snapshot();
    }
    internal NativeDiscoveryOmissionArm? PeekArm()
    { lock (sync) { RequestCqrsProbePaths.RequireDirectory(root, root); return Arm(); } }
    internal NativeDiscoveryOmissionWitness? PeekWitness(string name)
    { lock (sync) { RequestCqrsProbePaths.RequireDirectory(root, root); return Witness(name); } }
    internal T Locked<T>(Func<T> action)
    { lock (sync) { return LockedCore(action); } }
    private T LockedCore<T>(Func<T> action)
    {
        if (OperatingSystem.IsWindows())
        { throw Invalid(); }
        RequestCqrsProbePaths.RequireDirectory(root, root);
        var path = Path.Combine(root, NativeDiscoveryOmissionProtocol.LockFile);
        if (File.Exists(path))
        { _ = Measure(path); }
        using var ownership = new FileStream(Path.Combine(root, NativeDiscoveryOmissionProtocol.LockFile), new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            UnixCreateMode = RequestCqrsProbeProtocol.PrivateFileMode,
            BufferSize = options.Value.FileBufferBytes
        });
        _ = Snapshot();
        return action();
    }
    internal NativeDiscoveryOmissionArm? Arm()
    {
        var path = Path.Combine(root, NativeDiscoveryOmissionProtocol.ArmFile);
        if (!File.Exists(path))
        { return null; }
        var value = json.ReadArm(Read(path));
        if (value.SessionId != session || !voters.Contains(value.SourceVoter, StringComparer.Ordinal)
            || !voters.Contains(value.TargetVoter, StringComparer.Ordinal))
        { throw Invalid(); }
        return value;
    }
    internal NativeDiscoveryOmissionWitness? Witness(string name)
    {
        var path = Path.Combine(root, name);
        return File.Exists(path) ? json.ReadWitness(Read(path)) : null;
    }
    internal void Write(string name, NativeDiscoveryOmissionWitness value)
        => RequestCqrsProbeAtomicFiles.Write(root, Path.Combine(root, name), json.Write(value), options, Snapshot);

    private RequestCqrsProbeSnapshot Snapshot()
    {
        RequestCqrsProbePaths.RequireDirectory(root, root);
        var entries = Directory.EnumerateFileSystemEntries(root)
            .Take(options.Value.MaximumFiles + NativeDiscoveryOmissionProtocol.ExcessEntry).ToArray();
        if (entries.Length > options.Value.MaximumFiles)
        { throw Invalid(); }
        long bytes = NativeDiscoveryOmissionProtocol.EmptyCount;
        var files = NativeDiscoveryOmissionProtocol.EmptyCount;
        var owners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in entries)
        {
            var name = Path.GetFileName(path);
            var voter = voters.SingleOrDefault(value => new Uri(value).Host == name);
            if (voter is not null)
            {
                RequestCqrsProbePaths.RequireDirectory(path, path);
                var children = Directory.EnumerateFileSystemEntries(path).Take(NativeDiscoveryOmissionProtocol.ExcessEntry + NativeDiscoveryOmissionProtocol.ExcessEntry).ToArray();
                if (children.Length != NativeDiscoveryOmissionProtocol.ExcessEntry
                    || Path.GetFileName(children[NativeDiscoveryOmissionProtocol.EmptyCount]) != RequestCqrsProbeProtocol.OwnerFile)
                { throw Invalid(); }
                var ownerPath = children[NativeDiscoveryOmissionProtocol.EmptyCount];
                var owner = ownerJson.ReadOwner(Read(ownerPath));
                if (owner.SessionId != session || owner.Voter != voter || !owners.Add(voter))
                { throw Invalid(); }
                bytes = checked(bytes + Measure(ownerPath));
            }
            else
            {
                if (!Known(name))
                { throw Invalid(); }
                bytes = checked(bytes + Measure(path));
            }
            files = checked(files + NativeDiscoveryOmissionProtocol.ExcessEntry);
        }
        if (immutable.Keys.Any(path => !File.Exists(path)))
        { throw Invalid(); }
        if (owners.Count != voters.Length || files > options.Value.MaximumFiles || bytes > options.Value.MaximumAggregateBytes)
        { throw Invalid(); }
        return new([], [], [], files, bytes);
    }
    private long Measure(string path)
    {
        var identity = KeyLoad.Storage.IO.OfflineRegularFile.Inspect(path);
        RequestCqrsProbePaths.RequirePrivateMode(path, RequestCqrsProbeProtocol.PrivateFileMode);
        if (identity.Length > options.Value.MaximumRecordBytes
            || Path.GetFileName(path) == NativeDiscoveryOmissionProtocol.LockFile && identity.Length != NativeDiscoveryOmissionProtocol.EmptyCount)
        { throw Invalid(); }
        return identity.Length;
    }
    private byte[] Read(string path)
    {
        var current = RequestCqrsProbeFiles.ReadRecord(path, options);
        if (immutable.TryGetValue(path, out var prior) && !CryptographicOperations.FixedTimeEquals(prior, current))
        { throw Invalid(); }
        immutable.TryAdd(path, current.ToArray());
        return current;
    }
    private const string TemporarySuffix = ".tmp";
    private static bool Known(string name) => name is NativeDiscoveryOmissionProtocol.ArmFile
        or NativeDiscoveryOmissionProtocol.RequestFile or NativeDiscoveryOmissionProtocol.OmittedFile
        or NativeDiscoveryOmissionProtocol.VerifiedFile or NativeDiscoveryOmissionProtocol.LockFile
        || name.StartsWith(RequestCqrsProbeProtocol.TemporaryFilePrefix, StringComparison.Ordinal)
            && name.EndsWith(TemporarySuffix, StringComparison.Ordinal)
            && Guid.TryParseExact(name.AsSpan(RequestCqrsProbeProtocol.TemporaryFilePrefix.Length,
                name.Length - RequestCqrsProbeProtocol.TemporaryFilePrefix.Length - TemporarySuffix.Length), RequestCqrsProbeProtocol.SessionIdFormat, out var id)
            && id != Guid.Empty;
    private static InvalidOperationException Invalid() => new(NativeDiscoveryOmissionProtocol.Invalid);
}
