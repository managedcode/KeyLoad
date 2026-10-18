using System.Net;

namespace KeyLoad.Server;

/// <summary>Native Orleans hosting identity and the files exclusively owned by this silo.</summary>
[ConfigurationOptions]
internal sealed record NodeOptions
{
    private const string DefaultDirectory = "data/node";
    private const string DefaultPublicEndpoint = "http://127.0.0.1:5100";
    private const string DefaultClusterId = "keyload";
    private const string LoopbackAddress = "127.0.0.1";
    private const string RootCredentialPrefix = "root.";
    private const string InvalidIdentity = "The native silo identity or database credentials are invalid.";
    private const int InitialCount = 1;
    private const int DefaultSiloPort = 11_111;
    private const int SigningKeyBytes = 32;
    private const int MinimumAdministratorKeyCharacters = 32;

    /// <summary>Private directory containing this silo's ZoneTree files.</summary>
    public string DataDirectory { get; init; } = DefaultDirectory;
    /// <summary>Public API origin, independent of Orleans membership and data-copy selection.</summary>
    public string PublicEndpoint { get; init; } = DefaultPublicEndpoint;
    /// <summary>Native Orleans silo name.</summary>
    public string SiloName { get; init; } = Environment.MachineName;
    /// <summary>Native Orleans cluster identity.</summary>
    public string ClusterId { get; init; } = DefaultClusterId;
    /// <summary>Number of provisioned Orleans silos.</summary>
    public int SiloCount { get; init; } = InitialCount;
    /// <summary>Number of persisted data copies required for acknowledgement.</summary>
    public int ReplicaCount { get; init; } = InitialCount;
    /// <summary>Database incarnation, stable across silo restarts.</summary>
    public Guid Incarnation { get; init; }
    /// <summary>Database signing credential, shared only by trusted storage owners.</summary>
    public string SigningKey { get; init; } = string.Empty;
    /// <summary>Initial root credential, used only when the canonical store is fresh.</summary>
    public string AdminKey { get; init; } = string.Empty;
    /// <summary>Native Orleans silo port.</summary>
    public int SiloPort { get; init; } = DefaultSiloPort;
    /// <summary>Routable native silo address.</summary>
    public string SiloAddress { get; init; } = LoopbackAddress;
    /// <summary>Bounded command admission.</summary>
    public CommandAdmissionLimits CommandAdmission { get; init; } = new();
    /// <summary>Bounded public HTTP admission.</summary>
    public HttpAdmissionLimits HttpAdmission { get; init; } = new();
    /// <summary>Bounded MCP request, serialization and retained output pools.</summary>
    public McpMemoryLimits McpMemory { get; init; } = new();

    /// <summary>Rejects invalid startup settings before opening files.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(CommandAdmission);
        ArgumentNullException.ThrowIfNull(HttpAdmission);
        ArgumentNullException.ThrowIfNull(McpMemory);
        CommandAdmission.Validate();
        HttpAdmission.Validate();
        McpMemory.Validate();
        Span<byte> secret = stackalloc byte[SigningKeyBytes];
        if (Incarnation == Guid.Empty || string.IsNullOrWhiteSpace(DataDirectory)
            || SiloCount < InitialCount || ReplicaCount < InitialCount || ReplicaCount > SiloCount
            || string.IsNullOrWhiteSpace(SiloName) || string.IsNullOrWhiteSpace(ClusterId)
            || string.IsNullOrWhiteSpace(SiloAddress) || SiloPort is <= IPEndPoint.MinPort or > IPEndPoint.MaxPort
            || !Convert.TryFromBase64String(SigningKey, secret, out var length) || length != secret.Length
            || AdminKey is null || AdminKey.Length < MinimumAdministratorKeyCharacters
            || !AdminKey.StartsWith(RootCredentialPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(InvalidIdentity);
        }
    }
}
