using System.Globalization;
using System.Text;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Features.StorageRecovery;
using KeyLoad.AppHost.Hosting;

internal static class ClusterResources
{
    private const string CoordinationOnlyEnvironment = "KeyLoad__CoordinationOnly";
    private const string SiloCountEnvironment = "KeyLoad__SiloCount";
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
    private const string DevelopmentSingleVoterEnvironment = "KeyLoad__DevelopmentSingleVoter";
    private const int SingleFunctionalNodeCount = 1;
    private const int FirstNodeIndex = 0;
    private const string TrueValue = "true";
    private const int HttpPort = 8080;
    private const int SiloPort = 11111;

    /// <summary>Adds independently configured native silos and physical replicas, preserving explicit qualified topologies.</summary>
    internal static IResourceBuilder<ContainerResource>[] Add(IDistributedApplicationBuilder builder,
        LocalProfile profile, string dataRoot, bool ephemeral, int? benchmarkNodeCount = null, int? functionalNodeCount = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        var nodeNames = ClusterTopologyNodeNames.Read(benchmarkNodeCount, functionalNodeCount, ephemeral,
            AppHostOptionsRegistration.Get(builder).Cluster.Value.SiloCount);
        ClusterProfileStore.Validate(profile);
        var root = Path.GetFullPath(dataRoot);
        var imageOptions = AppHostOptionsRegistration.Get(builder).NativeCoverageClusterImage;
        var coverageImage = NativeCoverageClusterClusterImage.Read(imageOptions, ephemeral, benchmarkNodeCount);
        var (localImage, images) = ClusterRuntimeImages.Read(builder, ephemeral, benchmarkNodeCount,
            coverageImage, nodeNames);
        ClusterProfileStore.PrepareDirectory(root);
        var identity = AddIdentityParameters(builder, profile);
        var sourceImage = localImage is null && coverageImage is null && images is null
            ? SdkContainerImage.Add(builder, profile.Incarnation) : null;
        var (nodes, contenders) = AddNodes(builder, profile, root, ephemeral, benchmarkNodeCount, nodeNames, coverageImage,
            localImage, images, sourceImage, identity);
        if (functionalNodeCount == SingleFunctionalNodeCount)
        {
            nodes[FirstNodeIndex].WithEnvironment(DevelopmentSingleVoterEnvironment, TrueValue);
        }
        var tables = KeyLoad.AppHost.Features.ClusterRouting.NativeClusteringResources.Add(builder);
        foreach (var node in nodes.Concat(contenders))
        {
            KeyLoad.AppHost.Features.ClusterRouting.NativeClusteringResources.Apply(node, tables);
        }
        return nodes;
    }

    private static (IResourceBuilder<ContainerResource>[] Nodes, IResourceBuilder<ContainerResource>[] Contenders)
        AddNodes(IDistributedApplicationBuilder builder,
        LocalProfile profile, string root, bool ephemeral, int? benchmarkNodeCount, string[] nodeNames,
        NativeCoverageClusterClusterImage? coverageImage, LocalDevelopmentContainerImage? localImage,
        IReadOnlyDictionary<string, RuntimeContainerImage>? images, SdkContainerImage? sourceImage,
        (IResourceBuilder<ParameterResource> Signing, IResourceBuilder<ParameterResource> Peer,
            IResourceBuilder<ParameterResource> Admin, IResourceBuilder<ParameterResource> Incarnation) identity)
    {
        const int IndexInitialValue = 0;
        var physicalShardId = profile.PhysicalShardId.ToString(GuidFormat);
        var firstPublicPort = AppHostOptionsRegistration.Get(builder).Cluster.Value.FirstPublicPort;
        var containerUser = ClusterContainerUser.Resolve(builder, TimeProvider.System);
        var nodes = new IResourceBuilder<ContainerResource>[nodeNames.Length];
        for (var index = IndexInitialValue; index < nodes.Length; index++)
        {
            var name = nodeNames[index];
            nodes[index] = AddNode(builder, profile, root, ephemeral, benchmarkNodeCount, nodeNames,
                coverageImage, localImage, images, sourceImage, identity, name, index, physicalShardId, containerUser,
                firstPublicPort);
        }
        var contenders = ExclusiveOwnerContentionResources.Add(
            AppHostOptionsRegistration.Get(builder).Control.Value.ExclusiveOwnerContention, nodes[FirstNodeIndex],
            resourceName => AddNode(builder, profile, root, ephemeral, benchmarkNodeCount, nodeNames,
                coverageImage, localImage, images, sourceImage, identity, ExclusiveOwnerContentionResources.OwnerName,
                FirstNodeIndex, physicalShardId, containerUser, firstPublicPort, resourceName,
                Path.GetFullPath(Path.Combine(root, ExclusiveOwnerContentionResources.OwnerName))));
        return (nodes, contenders);
    }

    private static IResourceBuilder<ContainerResource> AddNode(IDistributedApplicationBuilder builder,
        LocalProfile profile, string root, bool ephemeral, int? benchmarkNodeCount, string[] nodeNames,
        NativeCoverageClusterClusterImage? coverageImage, LocalDevelopmentContainerImage? localImage,
        IReadOnlyDictionary<string, RuntimeContainerImage>? images, SdkContainerImage? sourceImage,
        (IResourceBuilder<ParameterResource> Signing, IResourceBuilder<ParameterResource> Peer,
            IResourceBuilder<ParameterResource> Admin, IResourceBuilder<ParameterResource> Incarnation) identity,
        string name, int index, string physicalShardId, string? containerUser, int firstPublicPort,
        string? resourceName = null, string? directoryOverride = null)
    {
        resourceName ??= name;
        var replicaCount = benchmarkNodeCount ?? AppHostOptionsRegistration.Get(builder).Control.Value.FunctionalNodeCount
            ?? AppHostOptionsRegistration.Get(builder).Cluster.Value.ReplicaCount;
        var ownsReplica = index < replicaCount;
        var directory = ownsReplica ? directoryOverride ?? PrepareVoterDirectory(root, name) : null;
        var coverageDirectory = coverageImage?.PrepareOutputDirectory(resourceName);
        var resource = localImage is null
            ? coverageImage is null
                ? images is null
                    ? sourceImage!.AddNode(builder, resourceName)
                    : images[name].Add(builder, resourceName)
                : builder.AddContainer(resourceName, NativeCoverageClusterProtocol.CoverageImageRepository,
                    coverageImage.Tag)
            : builder.AddContainer(resourceName, LocalDevelopmentContainerImage.Repository, localImage.Tag);
        resource
            .WithContainerName(string.Format(CultureInfo.InvariantCulture, ContainerNameCompositeFormat,
                profile.Incarnation.ToString(ClusterGuidFormat), resourceName))
            .WithContainerNetworkAlias(resourceName)
            .WithHttpEndpoint(targetPort: HttpPort, port: ephemeral ? null : firstPublicPort + index,
                name: HttpEndpoint, isProxied: false)
            .WithEndpoint(targetPort: SiloPort, name: SiloEndpoint, scheme: TcpScheme, isExternal: false, isProxied: false)
            .WithEnvironment(ClusterEnvironment, ClusterPrefix + profile.Incarnation.ToString(ClusterGuidFormat))
            .WithEnvironment(PublicEnvironment, Origin(name))
            .WithEnvironment(SiloAddressEnvironment, resourceName)
            .WithEnvironment(SiloPortEnvironment, SiloPort.ToString(CultureInfo.InvariantCulture))
            .WithEnvironment(HttpPortEnvironment, HttpPort.ToString(CultureInfo.InvariantCulture))
            .WithHttpHealthCheck(ReadyPath, endpointName: HttpEndpoint);
        if (coverageImage is not null)
        {
            resource.WithBindMount(coverageDirectory!, NativeCoverageClusterProtocol.CoverageOutputDirectory);
            coverageImage.Apply(resource, resourceName);
        }
        if (ownsReplica)
        {
            resource.WithBindMount(directory!, ContainerDirectory)
                .WithEnvironment(DataEnvironment, ContainerDirectory)
                .WithEnvironment(PhysicalShardEnvironment, physicalShardId)
                .WithEnvironment(IncarnationEnvironment, identity.Incarnation)
                .WithEnvironment(SigningEnvironment, identity.Signing).WithEnvironment(PeerEnvironment, identity.Peer)
                .WithEnvironment(AdminEnvironment, identity.Admin).WithEnvironment(PrivateHttpEnvironment, TrueValue);
            KeyLoad.AppHost.Features.ClientApi.ClusterConnectionEndpointResources.Apply(resource);
            ApplyPeers(resource, nodeNames.Take(replicaCount).ToArray(), benchmarkNodeCount.HasValue);
        }
        ClusterResourceSettings.Apply(builder, resource, containerUser);
        ContainerKillCommand.Apply(builder, resource, ephemeral, benchmarkNodeCount);
        if (!ownsReplica)
        {
            resource.WithEnvironment(CoordinationOnlyEnvironment, TrueValue)
                .WithEnvironment(SiloCountEnvironment, nodeNames.Length.ToString(CultureInfo.InvariantCulture));
        }
        return resource;
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

    private static string PrepareVoterDirectory(string root, string name)
    {
        var directory = Path.Combine(root, name);
        ClusterProfileStore.PrepareDirectory(directory);
        return directory;
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
