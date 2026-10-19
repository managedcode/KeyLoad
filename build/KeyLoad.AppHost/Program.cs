using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using KeyLoad.AppHost.Features.StorageRecovery;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Clustering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Hosting;

internal static class AppHostProgram
{
    private const int Completed = 0;
    private const string InvalidMode = "Comparison workloads belong to KeyLoad.LoadTests.AppHost; execution tests use their TUnit entry point.";
    private const string MembershipEndpoint = "membership";
    private const string MembershipParameter = "membership-secret";
    private const string MembershipSecretEnvironment = "KeyLoad__RapidMembership__SharedSecret";
    private const string MembershipPortEnvironment = "KeyLoad__RapidMembership__ListenPort";
    private const int MembershipSecretBytes = 32;
    private const string SiloCountEnvironment = "KeyLoad__SiloCount";
    private const string ReplicaCountEnvironment = "KeyLoad__ReplicaCount";
    private const string StorageModeEnvironment = "KeyLoad__StorageMode";
    private const string SharedDirectory = "shared";
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
    private const int SharedVolumeCopies = 1;

    private static async Task<int> Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);
        AddKeyLoad(builder);
        var application = builder.Build();
        if (AppHostOptionsRegistration.Get(builder).Control.Value.LoggerModelControl)
        {
            return Completed;
        }
        await using (application)
        {
            await application.RunAsync();
        }
        return Completed;
    }

    internal static void AddKeyLoad(IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton(TimeProvider.System);
        var runtime = AppHostOptionsRegistration.Get(builder);
        var control = runtime.Control.Value;
        if (control.TargetSelected || control.BenchmarksEnabled || control.ComparisonSelectorsPresent)
        {
            throw new InvalidOperationException(InvalidMode);
        }
        var configuration = global::AppHostConfiguration.Read(builder);
        var profile = global::ClusterProfileStore.Open(configuration.DataRoot, runtime.Profile);
        AddCluster(builder, profile, configuration.DataRoot, configuration.Ephemeral,
            functionalNodeCount: control.FunctionalNodeCount);
    }

    /// <summary>Adds execution project silos with the configured isolated or shared physical storage path.</summary>
    internal static IResourceBuilder<ProjectResource>[] AddCluster(IDistributedApplicationBuilder builder,
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
        if (replicas > nodeNames.Length
            || (options.Cluster.Value.StorageMode == StorageAccessMode.SharedVolume && replicas != SharedVolumeCopies))
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
        var membershipSecret = builder.AddParameter(MembershipParameter,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(MembershipSecretBytes)), secret: true);
        foreach (var node in nodes.Concat(contenders))
        {
            node.WithEndpoint(name: MembershipEndpoint, scheme: TcpScheme, isExternal: false, isProxied: false)
                .WithEnvironment(MembershipSecretEnvironment, membershipSecret)
                .WithEnvironment(MembershipPortEnvironment, node.GetEndpoint(MembershipEndpoint).Property(EndpointProperty.TargetPort));
        }
        return nodes;
    }

    private const string FileRootEnvironment = "KeyLoad__FileStorage__RootDirectory";
    private const string SharedFilesEnvironment = "KeyLoad__FileStorage__SharedDirectory";
    private const string SharedFilesDirectory = "files";

    private static IResourceBuilder<ProjectResource> AddNode(IDistributedApplicationBuilder builder,
        LocalProfile profile, string root, bool ephemeral, int siloCount, int replicaCount,
        (IResourceBuilder<ParameterResource> Signing, IResourceBuilder<ParameterResource> Admin,
            IResourceBuilder<ParameterResource> Incarnation) identity,
        string ownerName, int index, string? resourceName = null)
    {
        resourceName ??= ownerName;
        var options = AppHostOptionsRegistration.Get(builder).Cluster.Value;
        var directory = Path.Combine(root, options.StorageMode == StorageAccessMode.SharedVolume ? SharedDirectory : ownerName);
        ClusterProfileStore.PrepareDirectory(directory);
        var port = ephemeral ? (int?)null : options.FirstPublicPort + index;
        var resource = builder.AddProject<Projects.KeyLoad>(resourceName, launchProfileName: null)
            .WithHttpEndpoint(port: port, name: HttpEndpoint, isProxied: false)
            .WithEndpoint(name: SiloEndpoint, scheme: TcpScheme, isExternal: false, isProxied: false);
        resource.WithEnvironment(ClusterEnvironment, ClusterPrefix + profile.Incarnation.ToString(ClusterGuidFormat))
            .WithEnvironment(PublicEnvironment, resource.GetEndpoint(HttpEndpoint))
            .WithEnvironment(ServerUrlsEnvironment, resource.GetEndpoint(HttpEndpoint))
            .WithEnvironment(SiloNameEnvironment, resourceName)
            .WithEnvironment(SiloCountEnvironment, siloCount.ToString(CultureInfo.InvariantCulture))
            .WithEnvironment(ReplicaCountEnvironment, replicaCount.ToString(CultureInfo.InvariantCulture))
            .WithEnvironment(StorageModeEnvironment, options.StorageMode.ToString())
            .WithEnvironment(SiloAddressEnvironment, LoopbackAddress)
            .WithEnvironment(SiloPortEnvironment, resource.GetEndpoint(SiloEndpoint).Property(EndpointProperty.TargetPort))
            .WithEnvironment(DataEnvironment, directory)
            .WithEnvironment(FileRootEnvironment, Path.Combine(root, SharedFilesDirectory))
            .WithEnvironment(SharedFilesEnvironment, bool.TrueString)
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
