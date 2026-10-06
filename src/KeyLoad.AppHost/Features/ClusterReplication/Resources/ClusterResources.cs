using System.Globalization;
using System.Text;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.ClusterRouting;
using KeyLoad.AppHost.Hosting;

internal static class ClusterResources
{
    private const string ThirdVoterName = "node3";

    private const string NodeNamesResultText = "node1";
    private const string NodeNamesNodeNamesResultText = "node2";

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
    private const string PhysicalShardEnvironment = "KeyLoad__PhysicalShardId";
    private const string SigningEnvironment = "KeyLoad__SigningKey";
    private const string PeerEnvironment = "KeyLoad__PeerSecret";
    private const string AdminEnvironment = "KeyLoad__AdminKey";
    private const string PrivateHttpEnvironment = "KeyLoad__AllowPrivateNetworkHttp";
    private const string PublicEnvironment = "KeyLoad__PublicEndpoint";
    private const string SiloAddressEnvironment = "KeyLoad__SiloAddress";
    private const string SiloPortEnvironment = "KeyLoad__SiloPort";
    private const string HttpPortEnvironment = "ASPNETCORE_HTTP_PORTS";
    private const string PeerEnvironmentPrefix = "KeyLoad__Peers__";
    private const string BenchmarkTopologyEnvironment = "KeyLoad__BenchmarkTopology";
    private const string TrueValue = "true";
    private const int HttpPort = 8080;
    private const int SiloPort = 11111;
    private const int FirstPublicPort = 5101;
    private const int MinimumBenchmarkNodes = 1;
    private static readonly string[] NodeNames = [NodeNamesResultText, NodeNamesNodeNamesResultText, ThirdVoterName];

    /// <summary>Adds RF3 Docker nodes, or an explicitly selected benchmark fixed group, with independent storage.</summary>
    internal static IResourceBuilder<ContainerResource>[] Add(IDistributedApplicationBuilder builder,
        LocalProfile profile, string dataRoot, bool ephemeral, int? benchmarkNodeCount = null)
    {
        const int IndexInitialValue = 0;

        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        var nodeNames = ReadNodeNames(benchmarkNodeCount);
        ClusterProfileStore.Validate(profile);
        var root = Path.GetFullPath(dataRoot);
        var (localImage, images, probes) = ReadImagesAndProbes(builder, root, ephemeral, benchmarkNodeCount);
        ClusterProfileStore.PrepareDirectory(root);
        var (signing, peer, admin, incarnation) = AddIdentityParameters(builder, profile);
        var physicalShardId = profile.PhysicalShardId.ToString(GuidFormat);
        var containerUser = ClusterContainerUser.Resolve(builder);
        var nodes = new IResourceBuilder<ContainerResource>[nodeNames.Length];
        for (var index = IndexInitialValue; index < nodes.Length; index++)
        {
            var name = nodeNames[index];
            var directory = PrepareVoterDirectory(root, name);
            var resource = localImage is null
                ? images![name].Add(builder, name)
                : builder.AddContainer(name, LocalDevelopmentContainerImage.Repository, localImage.Tag);
            resource
                .WithContainerName(string.Format(CultureInfo.InvariantCulture, ContainerNameCompositeFormat,
                    profile.Incarnation.ToString(ClusterGuidFormat), name))
                .WithContainerNetworkAlias(name)
                .WithBindMount(directory, ContainerDirectory)
                .WithHttpEndpoint(targetPort: HttpPort, port: ephemeral ? null : FirstPublicPort + index,
                    name: HttpEndpoint, isProxied: false)
                .WithEndpoint(targetPort: SiloPort, name: SiloEndpoint, scheme: TcpScheme, isExternal: false, isProxied: false)
                .WithEnvironment(DataEnvironment, ContainerDirectory)
                .WithEnvironment(ClusterEnvironment, ClusterPrefix + profile.Incarnation.ToString(ClusterGuidFormat))
                .WithEnvironment(PhysicalShardEnvironment, physicalShardId)
                .WithEnvironment(IncarnationEnvironment, incarnation)
                .WithEnvironment(SigningEnvironment, signing).WithEnvironment(PeerEnvironment, peer)
                .WithEnvironment(AdminEnvironment, admin).WithEnvironment(PrivateHttpEnvironment, TrueValue)
                .WithEnvironment(PublicEnvironment, Origin(name))
                .WithEnvironment(SiloAddressEnvironment, name)
                .WithEnvironment(SiloPortEnvironment, SiloPort.ToString(CultureInfo.InvariantCulture))
                .WithEnvironment(HttpPortEnvironment, HttpPort.ToString(CultureInfo.InvariantCulture))
                .WithHttpHealthCheck(ReadyPath, endpointName: HttpEndpoint);
            ClusterResourceSettings.Apply(builder, resource, containerUser);
            probes?.Apply(resource, name);
            ApplyPeers(resource, nodeNames, benchmarkNodeCount.HasValue);
            nodes[index] = resource;
        }
        return nodes;
    }

    private static (IResourceBuilder<ParameterResource> Signing, IResourceBuilder<ParameterResource> Peer,
        IResourceBuilder<ParameterResource> Admin, IResourceBuilder<ParameterResource> Incarnation)
        AddIdentityParameters(IDistributedApplicationBuilder builder, LocalProfile profile)
    {
        var signing = builder.AddParameter(SigningParameter, profile.SigningKey, secret: true);
        var peer = builder.AddParameter(PeerParameter, profile.PeerSecret, secret: true);
        var admin = builder.AddParameter(AdminParameter, profile.AdminKey, secret: true);
        var incarnation = builder.AddParameter(IncarnationParameter, profile.Incarnation.ToString(GuidFormat), secret: true);
        return (signing, peer, admin, incarnation);
    }

    private static (LocalDevelopmentContainerImage? LocalImage,
        IReadOnlyDictionary<string, RuntimeContainerImage>? Images, RequestCqrsProbeProfile? Probes)
        ReadImagesAndProbes(IDistributedApplicationBuilder builder, string root, bool ephemeral,
            int? benchmarkNodeCount)
    {
        const string MessageText = "Local RF3 image mode requires the ordinary ephemeral RF3 topology.";
        const string ReadImagesAndProbesMessageText = "Local RF3 image mode cannot be combined with a protocol probe.";

        var localImage = LocalDevelopmentContainerImage.Read(builder);
        if (localImage is not null && (!ephemeral || benchmarkNodeCount is not null))
        {
            throw new InvalidOperationException(MessageText);
        }
        var images = localImage is null ? ProtocolCohortImages.Read(builder, ephemeral, benchmarkNodeCount) : null;
        var probes = localImage is null
            ? RequestCqrsProbeProfile.Read(builder, root, ephemeral, benchmarkNodeCount, images!)
            : AppHostOptionsRegistration.Get(builder).Control.Value.RequestProbe is null
                ? null
                : throw new InvalidOperationException(ReadImagesAndProbesMessageText);
        return (localImage, images, probes);
    }

    private static string PrepareVoterDirectory(string root, string name)
    {
        var directory = Path.Combine(root, name);
        ClusterProfileStore.PrepareDirectory(directory);
        return directory;
    }

    private static string[] ReadNodeNames(int? benchmarkNodeCount)
    {
        if (benchmarkNodeCount is null)
        {
            return NodeNames;
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(benchmarkNodeCount.Value, MinimumBenchmarkNodes, nameof(benchmarkNodeCount));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(benchmarkNodeCount.Value, NodeNames.Length, nameof(benchmarkNodeCount));
        return [.. NodeNames.Take(benchmarkNodeCount.Value)];
    }

    private static void ApplyPeers(IResourceBuilder<ContainerResource> resource, string[] nodeNames, bool benchmark)
    {
        const int IndexInitialValue = 0;

        if (benchmark)
        {
            resource.WithEnvironment(BenchmarkTopologyEnvironment, TrueValue);
        }
        for (var index = IndexInitialValue; index < nodeNames.Length; index++)
        {
            resource.WithEnvironment(PeerEnvironmentPrefix + index.ToString(CultureInfo.InvariantCulture), Origin(nodeNames[index]));
        }
    }

    internal static string Origin(string node) => string.Format(CultureInfo.InvariantCulture, OriginCompositeFormat,
        Uri.UriSchemeHttp, node, HttpPort);
}
