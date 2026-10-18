using System.Globalization;
using System.Net;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.StorageRecovery;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Options;

internal static class ClusterResources
{
    private const string SiloCountEnvironment = "KeyLoad__SiloCount";
    private const string ReplicaCountEnvironment = "KeyLoad__ReplicaCount";
    private const string SiloNameEnvironment = "KeyLoad__SiloName";
    private const string HttpEndpoint = "http";
    private const string SiloEndpoint = "silo";
    private const string TcpScheme = "tcp";
    private const string ReadyPath = "/health/ready";
    private const string SigningParameter = "signing-key";
    private const string AdminParameter = "admin-key";
    private const string IncarnationParameter = "incarnation";
    private const string GuidFormat = "D";
    private const string ClusterGuidFormat = "N";
    private const string ClusterPrefix = "keyload-";
    private const string DataEnvironment = "KeyLoad__DataDirectory";
    private const string ClusterEnvironment = "KeyLoad__ClusterId";
    private const string IncarnationEnvironment = "KeyLoad__Incarnation";
    private const string SigningEnvironment = "KeyLoad__SigningKey";
    private const string AdminEnvironment = "KeyLoad__AdminKey";
    private const string PublicEnvironment = "KeyLoad__PublicEndpoint";
    private const string SiloAddressEnvironment = "KeyLoad__SiloAddress";
    private const string SiloPortEnvironment = "KeyLoad__SiloPort";
    private const string ServerUrlsEnvironment = "ASPNETCORE_URLS";
    private const string LoopbackAddress = "127.0.0.1";
    private const int FirstNodeIndex = 0;
    private const int LastNodeOffset = 1;

    /// <summary>Adds real native project silos; each process retains its own canonical storage directory.</summary>
    internal static IResourceBuilder<ProjectResource>[] Add(IDistributedApplicationBuilder builder,
        LocalProfile profile, string dataRoot, bool ephemeral, int? benchmarkNodeCount = null, int? functionalNodeCount = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        var options = AppHostOptionsRegistration.Get(builder);
        var nodeNames = ClusterTopologyNodeNames.Read(benchmarkNodeCount, functionalNodeCount, ephemeral,
            options.Cluster.Value.SiloCount);
        var replicas = benchmarkNodeCount ?? options.Cluster.Value.ReplicaCount;
        if (!ephemeral && (long)options.Cluster.Value.FirstPublicPort + nodeNames.Length - LastNodeOffset > IPEndPoint.MaxPort)
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(ClusterDeploymentOptions),
                [ClusterDeploymentOptions.ValidationMessage]);
        }
        if (replicas > nodeNames.Length)
        {
            throw new InvalidOperationException(ClusterDeploymentOptions.ValidationMessage);
        }
        ClusterProfileStore.Validate(profile);
        var root = Path.GetFullPath(dataRoot);
        ClusterProfileStore.PrepareDirectory(root);
        var identity = AddIdentityParameters(builder, profile);
        var nodes = new IResourceBuilder<ProjectResource>[nodeNames.Length];
        for (var index = FirstNodeIndex; index < nodes.Length; index++)
        {
            nodes[index] = AddNode(builder, profile, root, ephemeral, nodeNames.Length,
                replicas, identity, nodeNames[index], index);
        }
        var contenders = ExclusiveOwnerContentionResources.Add(options.Control.Value.ExclusiveOwnerContention,
            nodes[FirstNodeIndex], resourceName => AddNode(builder, profile, root, true,
                nodeNames.Length, replicas, identity, ExclusiveOwnerContentionResources.OwnerName, FirstNodeIndex,
                resourceName));
        var tables = KeyLoad.AppHost.Features.ClusterRouting.NativeClusteringResources.Add(builder);
        foreach (var node in nodes.Concat(contenders))
        {
            KeyLoad.AppHost.Features.ClusterRouting.NativeClusteringResources.Apply(node, tables);
        }
        return nodes;
    }

    private static IResourceBuilder<ProjectResource> AddNode(IDistributedApplicationBuilder builder,
        LocalProfile profile, string root, bool ephemeral, int siloCount, int replicaCount,
        (IResourceBuilder<ParameterResource> Signing, IResourceBuilder<ParameterResource> Admin,
            IResourceBuilder<ParameterResource> Incarnation) identity,
        string ownerName, int index, string? resourceName = null)
    {
        resourceName ??= ownerName;
        var directory = Path.Combine(root, ownerName);
        ClusterProfileStore.PrepareDirectory(directory);
        var port = ephemeral ? (int?)null : AppHostOptionsRegistration.Get(builder).Cluster.Value.FirstPublicPort + index;
        var resource = builder.AddProject<Projects.KeyLoad_Server>(resourceName, launchProfileName: null)
            .WithHttpEndpoint(port: port, name: HttpEndpoint, isProxied: false)
            .WithEndpoint(name: SiloEndpoint, scheme: TcpScheme, isExternal: false, isProxied: false);
        resource.WithEnvironment(ClusterEnvironment, ClusterPrefix + profile.Incarnation.ToString(ClusterGuidFormat))
            .WithEnvironment(PublicEnvironment, resource.GetEndpoint(HttpEndpoint))
            .WithEnvironment(ServerUrlsEnvironment, resource.GetEndpoint(HttpEndpoint))
            .WithEnvironment(SiloNameEnvironment, resourceName)
            .WithEnvironment(SiloCountEnvironment, siloCount.ToString(CultureInfo.InvariantCulture))
            .WithEnvironment(ReplicaCountEnvironment, replicaCount.ToString(CultureInfo.InvariantCulture))
            .WithEnvironment(SiloAddressEnvironment, LoopbackAddress)
            .WithEnvironment(SiloPortEnvironment, resource.GetEndpoint(SiloEndpoint).Property(EndpointProperty.TargetPort))
            .WithEnvironment(DataEnvironment, directory)
            .WithEnvironment(IncarnationEnvironment, identity.Incarnation)
            .WithEnvironment(SigningEnvironment, identity.Signing)
            .WithEnvironment(AdminEnvironment, identity.Admin)
            .WithHttpHealthCheck(ReadyPath, endpointName: HttpEndpoint);
        KeyLoad.AppHost.Features.ClientApi.ClusterConnectionEndpointResources.Apply(resource);
        ClusterResourceSettings.Apply(builder, resource);
        ProcessKillCommand.Apply(builder, resource, ephemeral);
        return resource;
    }

    private static (IResourceBuilder<ParameterResource> Signing, IResourceBuilder<ParameterResource> Admin,
        IResourceBuilder<ParameterResource> Incarnation)
        AddIdentityParameters(IDistributedApplicationBuilder builder, LocalProfile profile)
    {
        var signing = builder.AddParameter(SigningParameter, profile.SigningKey, secret: true);
        var admin = builder.AddParameter(AdminParameter, profile.AdminKey, secret: true);
        var incarnation = builder.AddParameter(IncarnationParameter, profile.Incarnation.ToString(GuidFormat), secret: true);
        return (signing, admin, incarnation);
    }
}
