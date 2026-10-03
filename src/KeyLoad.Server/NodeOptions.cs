using System.Net;
using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.Server;

/// <summary>Fixed cluster configuration and private physical node settings.</summary>
internal sealed record NodeOptions
{
    /// <summary>Directory owned exclusively by one process, independent of grain placement.</summary>
    public string DataDirectory { get; init; } = NodeDefaults.DataDirectory;
    /// <summary>Stable configured voter identity and authenticated discovery HTTP origin.</summary>
    public string PublicEndpoint { get; init; } = NodeDefaults.PublicEndpoint;
    /// <summary>Fixed voter origins including the local origin; production requires an odd group of at least three.</summary>
    public IReadOnlyList<string> Peers { get; init; } = [];
    /// <summary>Trusted startup opt-in for isolated benchmark fixed groups of one, two or three voters.</summary>
    public bool BenchmarkTopology { get; init; }
    /// <summary>Orleans cluster identity, shared by every voter.</summary>
    public string ClusterId { get; init; } = NodeDefaults.ClusterId;
    /// <summary>Database incarnation, stable across process restarts and snapshot catch-up.</summary>
    public Guid Incarnation { get; init; }
    /// <summary>Base64 database signing credential; stored only in the private node catalog.</summary>
    public string SigningKey { get; init; } = string.Empty;
    /// <summary>Base64 peer HMAC credential; public callers cannot send replica control requests.</summary>
    public string PeerSecret { get; init; } = string.Empty;
    /// <summary>Initial root credential, used only when canonical storage is fresh.</summary>
    public string AdminKey { get; init; } = string.Empty;
    /// <summary>Native Orleans silo port; no public Orleans gateway is exposed.</summary>
    public int SiloPort { get; init; } = NodeDefaults.SiloPort;
    /// <summary>Routable silo IP or DNS name resolved on process startup.</summary>
    public string SiloAddress { get; init; } = NodeDefaults.SiloAddress;
    /// <summary>Permits plaintext discovery/public HTTP on explicit loopback development origins.</summary>
    public bool AllowLoopbackHttp { get; init; }
    /// <summary>Permits HTTP on the explicitly isolated Docker/Aspire development network.</summary>
    public bool AllowPrivateNetworkHttp { get; init; }
    /// <summary>Committed entries between canonical checkpoints.</summary>
    public int SnapshotThreshold { get; init; } = NodeDefaults.SnapshotThreshold;
    /// <summary>Lower randomized election bound, longer than an individual peer RPC.</summary>
    public int LowerElectionTimeoutMilliseconds { get; init; } = NodeDefaults.LowerElectionMilliseconds;
    /// <summary>Upper randomized election bound.</summary>
    public int UpperElectionTimeoutMilliseconds { get; init; } = NodeDefaults.UpperElectionMilliseconds;
    /// <summary>HTTP discovery connection establishment bound.</summary>
    public int PeerConnectTimeoutMilliseconds { get; init; } = NodeDefaults.ConnectMilliseconds;
    /// <summary>Overall Orleans peer RPC deadline, including bounded generation rediscovery.</summary>
    public int PeerRpcTimeoutMilliseconds { get; init; } = NodeDefaults.RpcMilliseconds;
    /// <summary>Data and reserved control admission limits.</summary>
    public CommandAdmissionLimits CommandAdmission { get; init; } = new();
    /// <summary>Public HTTP request body and concurrency limits.</summary>
    public HttpAdmissionLimits HttpAdmission { get; init; } = new();
    /// <summary>Independent MCP body, native serialization and retained-output byte pools.</summary>
    public McpMemoryLimits McpMemory { get; init; } = new();
    /// <summary>Independent bounded authenticated nonce pools per fixed voter.</summary>
    public ReplicaReplayLimits ReplayAdmission { get; init; } = new();

    /// <summary>Rejects invalid identity, timing, transport and admission settings before opening files.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(CommandAdmission);
        ArgumentNullException.ThrowIfNull(HttpAdmission);
        ArgumentNullException.ThrowIfNull(ReplayAdmission);
        ArgumentNullException.ThrowIfNull(McpMemory);
        CommandAdmission.Validate();
        HttpAdmission.Validate();
        McpMemory.Validate();
        ValidatePeers();
        if (Incarnation == Guid.Empty || SecretLength(SigningKey) != ReplicaTransportProtocol.SecretBytes
            || SecretLength(PeerSecret) != ReplicaTransportProtocol.SecretBytes
            || AdminKey is null || AdminKey.Length < NodeDefaults.MinimumAdminCharacters
            || !AdminKey.StartsWith(NodeDefaults.AdminPrefix, StringComparison.Ordinal)
            || SiloPort is <= IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            throw new InvalidOperationException(NodeDefaults.InvalidIdentity);
        }
        var configuration = CreateReplicaConfiguration(Path.GetFullPath(DataDirectory));
        configuration.Validate();
        CreatePeerOptions().Validate(configuration);
    }

    private void ValidatePeers()
    {
        if (Peers is null || (BenchmarkTopology
                ? Peers.Count is < NodeDefaults.MinimumBenchmarkVoters or > NodeDefaults.MaximumBenchmarkVoters
                : Peers.Count < NodeDefaults.MinimumVoters || Peers.Count % 2 == 0)
            || Peers.Distinct(StringComparer.Ordinal).Count() != Peers.Count
            || !Peers.Contains(PublicEndpoint, StringComparer.Ordinal)
            || string.IsNullOrWhiteSpace(DataDirectory) || string.IsNullOrWhiteSpace(SiloAddress))
        {
            throw new InvalidOperationException(NodeDefaults.InvalidPeers);
        }
        foreach (var peer in Peers)
        {
            if (!Uri.TryCreate(peer, UriKind.Absolute, out var uri) || !ValidOrigin(uri))
            {
                throw new InvalidOperationException(NodeDefaults.InvalidPeers);
            }
        }
        if (IPAddress.TryParse(SiloAddress, out var address)
            && (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
                || Peers.Any(peer => !new Uri(peer).IsLoopback) && IPAddress.IsLoopback(address)))
        {
            throw new InvalidOperationException(NodeDefaults.InvalidAddress);
        }
    }

    private bool ValidOrigin(Uri uri) => (uri.Scheme == Uri.UriSchemeHttps
        || uri.Scheme == Uri.UriSchemeHttp && (AllowPrivateNetworkHttp || AllowLoopbackHttp && uri.IsLoopback))
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && uri.AbsolutePath == NodeDefaults.OriginPath;

    private static int SecretLength(string? encoded)
    {
        if (encoded is null)
        { return 0; }
        Span<byte> bytes = stackalloc byte[ReplicaTransportProtocol.SecretBytes];
        return Convert.TryFromBase64String(encoded, bytes, out var count) ? count : 0;
    }

    /// <summary>Creates the immutable replica scope without storage or an Orleans client.</summary>
    /// <param name="directory">The full physical node directory.</param>
    public ReplicaConfiguration CreateReplicaConfiguration(string directory) => new(PublicEndpoint, [.. Peers], directory, Incarnation)
    {
        BenchmarkTopology = BenchmarkTopology,
        SnapshotThreshold = SnapshotThreshold,
        LowerElectionTimeout = TimeSpan.FromMilliseconds(LowerElectionTimeoutMilliseconds),
        UpperElectionTimeout = TimeSpan.FromMilliseconds(UpperElectionTimeoutMilliseconds),
        RpcTimeout = TimeSpan.FromMilliseconds(PeerRpcTimeoutMilliseconds)
    };

    /// <summary>Creates fixed discovery endpoints and bounded HMAC replay limits.</summary>
    public ReplicaPeerOptions CreatePeerOptions() => new(Peers.ToDictionary(peer => peer, peer => new Uri(peer), StringComparer.Ordinal),
        Convert.FromBase64String(PeerSecret), ClusterId)
    {
        ConnectTimeout = TimeSpan.FromMilliseconds(PeerConnectTimeoutMilliseconds),
        ReplayLimits = ReplayAdmission,
        MaxControlPayloadBytes = CommandAdmission.MaxControlPayloadBytes
    };
}

internal static class NodeDefaults
{
    internal const string DataDirectory = "data/node";
    internal const string PublicEndpoint = "http://127.0.0.1:5100";
    internal const string ClusterId = "keyload";
    internal const string SiloAddress = "127.0.0.1";
    internal const string AdminPrefix = "root.";
    internal const string OriginPath = "/";
    internal const string InvalidIdentity = "Cluster identity, credentials or silo port are invalid.";
    internal const string InvalidPeers = "Cluster origins require distinct fixed voters, private development HTTP or HTTPS, and a local member.";
    internal const string InvalidAddress = "A remote voter requires a routable advertised silo address.";
    internal const int MinimumVoters = 3;
    internal const int MinimumBenchmarkVoters = 1;
    internal const int MaximumBenchmarkVoters = 3;
    internal const int SiloPort = 11_111;
    internal const int SnapshotThreshold = 1_024;
    internal const int LowerElectionMilliseconds = 4_000;
    internal const int UpperElectionMilliseconds = 8_000;
    internal const int ConnectMilliseconds = 500;
    internal const int RpcMilliseconds = 2_000;
    internal const int MinimumAdminCharacters = 32;
}
