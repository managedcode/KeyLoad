using System.Net;

namespace KeyLoad.Server;

/// <summary>Native Orleans hosting identity and the files exclusively owned by this silo.</summary>
[ConfigurationOptions]
internal sealed record NodeOptions
{
    /// <summary>Private directory containing this silo's ZoneTree files.</summary>
    public string DataDirectory { get; init; } = "data/node";
    /// <summary>Public API origin, independent of Orleans membership and data-copy selection.</summary>
    public string PublicEndpoint { get; init; } = "http://127.0.0.1:5100";
    /// <summary>Native Orleans silo name.</summary>
    public string SiloName { get; init; } = Environment.MachineName;
    /// <summary>Native Orleans cluster identity.</summary>
    public string ClusterId { get; init; } = "keyload";
    /// <summary>Number of provisioned Orleans silos.</summary>
    public int SiloCount { get; init; } = 1;
    /// <summary>Number of persisted data copies required for acknowledgement.</summary>
    public int ReplicaCount { get; init; } = 1;
    /// <summary>Database incarnation, stable across silo restarts.</summary>
    public Guid Incarnation { get; init; }
    /// <summary>Database signing credential, shared only by trusted storage owners.</summary>
    public string SigningKey { get; init; } = string.Empty;
    /// <summary>Initial root credential, used only when the canonical store is fresh.</summary>
    public string AdminKey { get; init; } = string.Empty;
    /// <summary>Native Orleans silo port.</summary>
    public int SiloPort { get; init; } = 11_111;
    /// <summary>Routable native silo address.</summary>
    public string SiloAddress { get; init; } = "127.0.0.1";
    /// <summary>Bounded command admission.</summary>
    public CommandAdmissionLimits CommandAdmission { get; init; } = new();
    /// <summary>Bounded public HTTP admission.</summary>
    public HttpAdmissionLimits HttpAdmission { get; init; } = new();
    /// <summary>Bounded MCP request, serialization and retained output pools.</summary>
    public McpMemoryLimits McpMemory { get; init; } = new();
    /// <summary>Persisted technical subject for authorized queue transfer, when configured.</summary>
    public string? RemoteTransferPrincipalId { get; init; }

    /// <summary>Rejects invalid startup settings before opening files.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(CommandAdmission);
        ArgumentNullException.ThrowIfNull(HttpAdmission);
        ArgumentNullException.ThrowIfNull(McpMemory);
        CommandAdmission.Validate();
        HttpAdmission.Validate();
        McpMemory.Validate();
        new KeyLoad.Clustering.ReplicaCopyOptions { SiloCount = SiloCount, ReplicaCount = ReplicaCount }.Validate();
        Span<byte> secret = stackalloc byte[32];
        if (Incarnation == Guid.Empty || string.IsNullOrWhiteSpace(DataDirectory)
            || string.IsNullOrWhiteSpace(SiloName) || string.IsNullOrWhiteSpace(ClusterId)
            || string.IsNullOrWhiteSpace(SiloAddress) || SiloPort is <= IPEndPoint.MinPort or > IPEndPoint.MaxPort
            || !Convert.TryFromBase64String(SigningKey, secret, out var length) || length != secret.Length
            || AdminKey is null || AdminKey.Length < 32 || !AdminKey.StartsWith("root.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The native silo identity or database credentials are invalid.");
        }
        if (RemoteTransferPrincipalId is not null)
        {
            KeyLoad.Core.JsonData.Identifier(RemoteTransferPrincipalId);
        }
    }
}
