using System.Text;

namespace KeyLoad.Clustering;

/// <summary>Host-selected in-memory Orleans membership discovery and authentication.</summary>
[ConfigurationOptions]
public sealed class OrleansHostingOptions
{
    private const int MinimumSharedSecretBytes = 32;
    private const int AutomaticListenPort = 0;
    private const int MaximumListenPort = 65535;
    /// <summary>Configuration shared by silos in one authenticated discovery group.</summary>
    public const string SectionName = "KeyLoad:RapidMembership";
    /// <summary>Startup refusal for invalid membership configuration.</summary>
    public const string ValidationMessage = "Rapid membership requires a shared secret of at least 32 UTF-8 bytes and a valid TCP port.";

    /// <summary>Gets or sets the independent membership authentication secret.</summary>
    public string SharedSecret { get; set; } = string.Empty;
    /// <summary>Gets or sets the private TCP listener port; zero selects an available port.</summary>
    public int ListenPort { get; set; }
    /// <summary>Gets or sets optional IPv4 CIDR networks or address ranges to discover.</summary>
    public IReadOnlyList<string> DiscoveryNetworks { get; set; } = [];

    /// <summary>Checks required authentication and listener configuration.</summary>
    /// <returns>Whether the configuration can be used by the membership provider.</returns>
    public bool IsValid() => !string.IsNullOrWhiteSpace(SharedSecret)
        && Encoding.UTF8.GetByteCount(SharedSecret) >= MinimumSharedSecretBytes
        && ListenPort is >= AutomaticListenPort and <= MaximumListenPort && DiscoveryNetworks is not null;
}
