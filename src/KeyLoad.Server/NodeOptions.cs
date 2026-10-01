namespace KeyLoad.Server;

public sealed record NodeOptions
{
    public string DataDirectory { get; init; } = "data/node";
    public string PublicEndpoint { get; init; } = "http://127.0.0.1:5100";
    public string[] Peers { get; init; } = [];
    public string ClusterId { get; init; } = "keyload";
    public Guid Incarnation { get; init; }
    public string SigningKey { get; init; } = "";
    public string PeerSecret { get; init; } = "";
    public string AdminKey { get; init; } = "";
    public int SiloPort { get; init; } = 11111;
    public string SiloAddress { get; init; } = "127.0.0.1";
    public bool AllowLoopbackHttp { get; init; }
    public int SnapshotThreshold { get; init; } = 1_024;
    public int LowerElectionTimeoutMilliseconds { get; init; } = 4_000;
    public int UpperElectionTimeoutMilliseconds { get; init; } = 8_000;
    public int PeerConnectTimeoutMilliseconds { get; init; } = 500;
    public int RaftRpcTimeoutMilliseconds { get; init; } = 1_500;
    public int RaftRequestTimeoutMilliseconds { get; init; } = 2_000;
    public CommandAdmissionLimits CommandAdmission { get; init; } = new();
    public HttpAdmissionLimits HttpAdmission { get; init; } = new();
    public void Validate()
    {
        CommandAdmission.Validate();
        HttpAdmission.Validate();
        if (Peers.Length < 3 || Peers.Length % 2 == 0 || Peers.Distinct(StringComparer.Ordinal).Count() != Peers.Length)
            throw new InvalidOperationException("Cluster configuration requires an odd number of at least three distinct voters.");
        if (!Peers.Contains(PublicEndpoint, StringComparer.Ordinal)) throw new InvalidOperationException("The public endpoint must be in the voter list.");
        foreach (var peer in Peers)
        {
            var uri = new Uri(peer, UriKind.Absolute);
            if (uri.Scheme != "https" && !(AllowLoopbackHttp && uri.Scheme == "http" && uri.IsLoopback))
                throw new InvalidOperationException("Cluster endpoints require HTTPS. Explicit loopback HTTP is available for local development.");
        }
        if (!System.Net.IPAddress.TryParse(SiloAddress, out var siloAddress)
            || siloAddress.Equals(System.Net.IPAddress.Any) || siloAddress.Equals(System.Net.IPAddress.IPv6Any)
            || Peers.Any(peer => !new Uri(peer).IsLoopback) && System.Net.IPAddress.IsLoopback(siloAddress))
            throw new InvalidOperationException("SiloAddress must be a routable advertised IP address when using remote peers.");
        if (PeerConnectTimeoutMilliseconds < 1 || PeerConnectTimeoutMilliseconds >= RaftRpcTimeoutMilliseconds
            || RaftRpcTimeoutMilliseconds >= RaftRequestTimeoutMilliseconds || RaftRequestTimeoutMilliseconds >= LowerElectionTimeoutMilliseconds
            || LowerElectionTimeoutMilliseconds >= UpperElectionTimeoutMilliseconds)
            throw new InvalidOperationException("Peer connect, RPC and request timeouts must be shorter than the lower election timeout; the election range must be ordered.");
        if (Incarnation == Guid.Empty || Convert.FromBase64String(SigningKey).Length != 32
            || Convert.FromBase64String(PeerSecret).Length != 32 || AdminKey.Length < 32 || !AdminKey.StartsWith("root.", StringComparison.Ordinal)
            || SiloPort is < 1 or > 65535 || SnapshotThreshold < 2) throw new InvalidOperationException("Cluster identity, secrets, silo port or snapshot threshold are missing or invalid.");
    }
}
