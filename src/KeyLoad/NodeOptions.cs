using System.Net;
using KeyLoad.Clustering;

namespace KeyLoad.Server;

/// <summary>Execution Orleans hosting identity and physical storage access.</summary>
[ConfigurationOptions]
internal sealed record NodeOptions
{
    private const string DefaultDirectory = "data/node";
    private const string DefaultPublicEndpoint = "http://127.0.0.1:5100";
    private const string DefaultClusterId = "keyload";
    private const string LoopbackAddress = "127.0.0.1";
    private const string RootCredentialPrefix = "root.";
    private const string InvalidIdentity = "The execution silo identity or database credentials are invalid.";
    private const int InitialCount = 1;
    private const int DefaultSiloPort = 11_111;
    private const int SigningKeyBytes = 32;
    private const int MinimumAdministratorKeyCharacters = 32;

    /// <summary>Configured directory containing the physical ZoneTree files.</summary>
    public string DataDirectory { get; init; } = DefaultDirectory;
    /// <summary>Public API origin, independent of Orleans membership and data-copy selection.</summary>
    public string PublicEndpoint { get; init; } = DefaultPublicEndpoint;
    /// <summary>Execution Orleans silo name.</summary>
    public string SiloName { get; init; } = Environment.MachineName;
    /// <summary>Execution Orleans cluster identity.</summary>
    public string ClusterId { get; init; } = DefaultClusterId;
    /// <summary>Number of provisioned Orleans silos.</summary>
    public int SiloCount { get; init; } = InitialCount;
    /// <summary>Whether silo owners access separate files or one shared physical store.</summary>
    public StorageAccessMode StorageMode { get; init; } = StorageAccessMode.IsolatedVolumes;
    /// <summary>Number of persisted data copies required for acknowledgement.</summary>
    public int ReplicaCount { get; init; } = InitialCount;
    /// <summary>Database incarnation, stable across silo restarts.</summary>
    public Guid Incarnation { get; init; }
    /// <summary>Database signing credential, shared only by trusted storage owners.</summary>
    public string SigningKey { get; init; } = string.Empty;
    /// <summary>Initial root credential, used only when the canonical store is fresh.</summary>
    public string AdminKey { get; init; } = string.Empty;
    /// <summary>Execution Orleans silo port.</summary>
    public int SiloPort { get; init; } = DefaultSiloPort;
    /// <summary>Routable execution silo address.</summary>
    public string SiloAddress { get; init; } = LoopbackAddress;

    /// <summary>Rejects invalid startup settings before opening files.</summary>
    public void Validate()
    {
        Span<byte> secret = stackalloc byte[SigningKeyBytes];
        if (Incarnation == Guid.Empty || string.IsNullOrWhiteSpace(DataDirectory)
            || SiloCount < InitialCount || ReplicaCount < InitialCount || ReplicaCount > SiloCount
            || !Enum.IsDefined(StorageMode) || (StorageMode == StorageAccessMode.SharedVolume && ReplicaCount != InitialCount)
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
