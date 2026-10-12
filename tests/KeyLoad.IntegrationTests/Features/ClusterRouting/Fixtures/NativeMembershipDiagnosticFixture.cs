using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Owns six private owner-only controls for authenticated native membership observation.</summary>
internal sealed class NativeMembershipDiagnosticFixture
{
    private const string RootPrefix = "keyload-native-membership-";
    private readonly System.Threading.Lock sync = new();
    private readonly Dictionary<string, byte[]> owners = new(StringComparer.Ordinal);
    private bool admissionStopped;
    private bool retained;
    private bool cleaned;

    private NativeMembershipDiagnosticFixture(string root, string sessionId)
    { Root = root; SessionId = sessionId; }

    internal string Root { get; }
    internal string SessionId { get; }

    internal static NativeMembershipDiagnosticFixture Create(string root, Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        { throw new ArgumentException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry, nameof(sessionId)); }
        if (OperatingSystem.IsWindows())
        { throw new PlatformNotSupportedException(RequestCqrsProbeFixtureProtocol.PrivatePermissionsUnsupported); }
        var session = sessionId.ToString("N");
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), RootPrefix + session));
        var data = Path.GetFullPath(root);
        if (Directory.Exists(path) || File.Exists(path)
            || RequestCqrsProbeFileValidation.IsWithin(path, data)
            || RequestCqrsProbeFileValidation.IsWithin(data, path))
        { throw new IOException(RequestCqrsProbeFixtureProtocol.OwnershipConflict); }
        var fixture = new NativeMembershipDiagnosticFixture(path, session);
        Directory.CreateDirectory(path, RequestCqrsProbeFileValidation.PrivateDirectoryMode);
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(fixture.CreateOwners, failures);
        if (failures.Count > 0)
        {
            ServerFailureObserver.Observe(() => fixture.DeleteOwnedTree(requireAll: false), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        return fixture;
    }

    private void CreateOwners()
    {
        if (OperatingSystem.IsWindows())
        { throw new PlatformNotSupportedException(RequestCqrsProbeFixtureProtocol.PrivatePermissionsUnsupported); }
        RequestCqrsProbeFileValidation.ValidateDirectory(Root);
        var json = new RequestCqrsProbeJson(IntegrationRoutingOptions.ProbeExecution());
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var voter = "http://" + node + ":8080";
            var bytes = RequestCqrsProbeJsonWriter.Owner(SessionId, voter);
            var owner = json.ReadOwner(bytes);
            if (owner.Version != RequestCqrsProbeFixtureProtocol.Version
                || owner.Kind != RequestCqrsProbeFixtureProtocol.OwnerKind
                || owner.SessionId != SessionId || owner.Voter != voter)
            { throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry); }
            var directory = Path.Combine(Root, node);
            if (Directory.Exists(directory) || File.Exists(directory))
            { throw new IOException(RequestCqrsProbeFixtureProtocol.OwnershipConflict); }
            Directory.CreateDirectory(directory, RequestCqrsProbeFileValidation.PrivateDirectoryMode);
            owners.Add(node, bytes);
            RequestCqrsProbeFileValidation.ValidateDirectory(directory);
            RequestCqrsProbeFileStore.WriteAtomic(directory, RequestCqrsProbeFixtureProtocol.OwnerFileName, bytes);
        }
    }

    internal void StopAdmission()
    { lock (sync) { admissionStopped = true; } }

    internal void RetainEvidence()
    { lock (sync) { retained = true; } }

    internal Task DisposeAfterOwnersJoinedAsync(TwoRf3MembershipWave wave)
    {
        lock (sync)
        {
            if (!admissionStopped || !wave.applicationDisposed || !wave.nodeLocksReleased)
            { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.UnsettledGates); }
            if (cleaned)
            { return Task.CompletedTask; }
            RequireAllLocks(wave);
            if (retained)
            { return Task.CompletedTask; }
            DeleteOwnedTree(requireAll: true);
            cleaned = true;
            return Task.CompletedTask;
        }
    }

    private static void RequireAllLocks(TwoRf3MembershipWave wave)
    {
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var root = Path.Combine(wave.OwnedDataRoot, node);
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, "node.owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, "database", "owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, "replica", "owner.lock"));
        }
    }

    private void DeleteOwnedTree(bool requireAll)
    {
        RequestCqrsProbeFileValidation.ValidateDirectory(Root);
        if (requireAll && owners.Count != TwoRf3MembershipProtocol.NodeCount)
        { throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry); }
        var entries = Directory.EnumerateFileSystemEntries(Root).Take(TwoRf3MembershipProtocol.NodeCount + 1).ToArray();
        if (entries.Length != owners.Count || entries.Any(path => !owners.ContainsKey(Path.GetFileName(path))))
        { throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry); }
        foreach (var (node, bytes) in owners)
        { VerifyOwnerOnly(node, bytes, requireAll); }
        foreach (var node in owners.Keys)
        {
            var directory = Path.Combine(Root, node);
            var owner = Path.Combine(directory, RequestCqrsProbeFixtureProtocol.OwnerFileName);
            if (File.Exists(owner))
            { RequestCqrsProbeFileStore.DeleteExactFile(directory, RequestCqrsProbeFixtureProtocol.OwnerFileName, owners[node]); }
            Directory.Delete(directory, recursive: false);
        }
        Directory.Delete(Root, recursive: false);
    }

    private void VerifyOwnerOnly(string node, byte[] bytes, bool requireOwner)
    {
        if (!TwoRf3MembershipProtocol.Nodes.Contains(node, StringComparer.Ordinal))
        { throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry); }
        var directory = Path.Combine(Root, node);
        RequestCqrsProbeFileValidation.ValidateDirectory(directory);
        var files = Directory.EnumerateFileSystemEntries(directory).Take(2).ToArray();
        var expected = Path.Combine(directory, RequestCqrsProbeFixtureProtocol.OwnerFileName);
        if (files.Length == 0 && !requireOwner)
        { return; }
        if (files.Length != 1 || !string.Equals(files[0], expected, StringComparison.Ordinal))
        { throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry); }
        RequestCqrsProbeFileStore.VerifyOwnerFile(directory, bytes);
    }
}
