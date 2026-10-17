using System.Net;

namespace KeyLoad.Server;

/// <summary>Native cluster participation without owning a physical database replica.</summary>
[ConfigurationOptions]
internal sealed record CoordinationSiloOptions
{
    internal const string InvalidSettings = "Native coordination silo settings are invalid.";
    private const string OriginPath = "/";
    private const int MinimumSilos = 1;
    public bool CoordinationOnly { get; init; }
    public string ClusterId { get; init; } = string.Empty;
    public string PublicEndpoint { get; init; } = string.Empty;
    public string SiloAddress { get; init; } = string.Empty;
    public int SiloCount { get; init; }
    public int SiloPort { get; init; }

    internal bool IsValid() => SiloCount >= MinimumSilos && !string.IsNullOrWhiteSpace(ClusterId)
        && Uri.TryCreate(PublicEndpoint, UriKind.Absolute, out var origin)
        && (origin.Scheme == Uri.UriSchemeHttp || origin.Scheme == Uri.UriSchemeHttps) && string.IsNullOrEmpty(origin.UserInfo)
        && string.IsNullOrEmpty(origin.Query) && string.IsNullOrEmpty(origin.Fragment) && origin.AbsolutePath == OriginPath
        && !string.IsNullOrWhiteSpace(SiloAddress) && SiloPort is > IPEndPoint.MinPort and <= IPEndPoint.MaxPort;
}
