using System.Globalization;
using System.Text;
using KeyLoad.AppHost.Features.ClusterReplication;

internal static class ClusterResources
{
    private const string ContainerDirectory = "/data";
    private const string HttpEndpoint = "http";
    private const string SiloEndpoint = "silo";
    private const string TcpScheme = "tcp";
    private const string OriginFormat = "{0}://{1}:{2}";
    private const string ReadyPath = "/health/ready";
    private const string SigningParameter = "signing-key";
    private const string PeerParameter = "peer-secret";
    private const string AdminParameter = "admin-key";
    private const string IncarnationParameter = "incarnation";
    private const string GuidFormat = "D";
    private const string ClusterGuidFormat = "N";
    private const string ClusterPrefix = "keyload-";
    private const string ContainerNameFormat = "keyload-{0}-{1}";
    private static readonly CompositeFormat ContainerNameCompositeFormat = CompositeFormat.Parse(ContainerNameFormat);
    private static readonly CompositeFormat OriginCompositeFormat = CompositeFormat.Parse(OriginFormat);
    private const string DataEnvironment = "KeyLoad__DataDirectory";
    private const string ClusterEnvironment = "KeyLoad__ClusterId";
    private const string IncarnationEnvironment = "KeyLoad__Incarnation";
    private const string SigningEnvironment = "KeyLoad__SigningKey";
    private const string PeerEnvironment = "KeyLoad__PeerSecret";
    private const string AdminEnvironment = "KeyLoad__AdminKey";
    private const string PrivateHttpEnvironment = "KeyLoad__AllowPrivateNetworkHttp";
    private const string PublicEnvironment = "KeyLoad__PublicEndpoint";
    private const string SiloAddressEnvironment = "KeyLoad__SiloAddress";
    private const string SiloPortEnvironment = "KeyLoad__SiloPort";
    private const string HttpPortEnvironment = "ASPNETCORE_HTTP_PORTS";
    private const string PeerEnvironmentPrefix = "KeyLoad__Peers__";
    private const string TrueValue = "true";
    private const int HttpPort = 8080;
    private const int SiloPort = 11111;
    private const int FirstPublicPort = 5101;
    private static readonly string[] NodeNames = ["node1", "node2", "node3"];

    /// <summary>Adds simultaneous RF3 Docker nodes with stable internal origins and independently mounted storage.</summary>
    internal static IResourceBuilder<ContainerResource>[] Add(IDistributedApplicationBuilder builder,
        LocalProfile profile, string dataRoot, bool ephemeral)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ClusterProfileStore.Validate(profile);
        var root = Path.GetFullPath(dataRoot);
        ClusterProfileStore.PrepareDirectory(root);
        var image = RuntimeContainerImage.Read(builder, RuntimeContainerImage.ServerConfiguration);
        var signing = builder.AddParameter(SigningParameter, profile.SigningKey, secret: true);
        var peer = builder.AddParameter(PeerParameter, profile.PeerSecret, secret: true);
        var admin = builder.AddParameter(AdminParameter, profile.AdminKey, secret: true);
        var incarnation = builder.AddParameter(IncarnationParameter, profile.Incarnation.ToString(GuidFormat), secret: true);
        var containerUser = ClusterContainerUser.Resolve(builder);
        var nodes = new IResourceBuilder<ContainerResource>[NodeNames.Length];
        for (var index = 0; index < nodes.Length; index++)
        {
            var name = NodeNames[index];
            var directory = Path.Combine(root, name);
            ClusterProfileStore.PrepareDirectory(directory);
            var resource = image.Add(builder, name)
                .WithContainerName(string.Format(CultureInfo.InvariantCulture, ContainerNameCompositeFormat,
                    profile.Incarnation.ToString(ClusterGuidFormat), name))
                .WithContainerNetworkAlias(name)
                .WithBindMount(directory, ContainerDirectory)
                .WithHttpEndpoint(targetPort: HttpPort, port: ephemeral ? null : FirstPublicPort + index,
                    name: HttpEndpoint, isProxied: false)
                .WithEndpoint(targetPort: SiloPort, name: SiloEndpoint, scheme: TcpScheme, isExternal: false, isProxied: false)
                .WithEnvironment(DataEnvironment, ContainerDirectory)
                .WithEnvironment(ClusterEnvironment, ClusterPrefix + profile.Incarnation.ToString(ClusterGuidFormat))
                .WithEnvironment(IncarnationEnvironment, incarnation)
                .WithEnvironment(SigningEnvironment, signing).WithEnvironment(PeerEnvironment, peer)
                .WithEnvironment(AdminEnvironment, admin).WithEnvironment(PrivateHttpEnvironment, TrueValue)
                .WithEnvironment(PublicEnvironment, Origin(name))
                .WithEnvironment(SiloAddressEnvironment, name)
                .WithEnvironment(SiloPortEnvironment, SiloPort.ToString(CultureInfo.InvariantCulture))
                .WithEnvironment(HttpPortEnvironment, HttpPort.ToString(CultureInfo.InvariantCulture))
                .WithHttpHealthCheck(ReadyPath, endpointName: HttpEndpoint);
            ClusterResourceSettings.Apply(builder, resource, containerUser);
            for (var peerIndex = 0; peerIndex < NodeNames.Length; peerIndex++)
            { resource.WithEnvironment(PeerEnvironmentPrefix + peerIndex.ToString(CultureInfo.InvariantCulture), Origin(NodeNames[peerIndex])); }
            nodes[index] = resource;
        }
        return nodes;
    }

    private static string Origin(string node) => string.Format(CultureInfo.InvariantCulture, OriginCompositeFormat,
        Uri.UriSchemeHttp, node, HttpPort);
}
