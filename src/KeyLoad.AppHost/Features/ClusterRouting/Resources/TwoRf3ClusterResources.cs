using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Hosting;

namespace KeyLoad.AppHost.Features.ClusterRouting;

internal static class TwoRf3ClusterResources
{
    private const string SigningKeyParameterName = "signing-key";
    private const string AdminKeyParameterName = "admin-key";
    private const string MembershipPeerParameterName = "membership-peer-a";
    private const string PeerParameterName = "peer-b";
    private const string ForeignPhysicalEnvironment = "ForeignPhysicalShardId";
    private const string ForeignIncarnationEnvironment = "ForeignIncarnation";
    private const string ForeignSecretEnvironment = "ForeignPeerSecret";
    private const string ForeignVotersEnvironment = "ForeignVoterIds";
    private const string RoleEnvironment = "Role";
    private const string ControlRole = "Control";
    private const string DestinationRole = "Destination";

    private const string PublicOriginTemplate = "http://{0}:8080";

    private const string ParameterIdentityFormat = "D";
    private const string ResourceIdentityFormat = "N";

    private const string Data = "/data";
    private const string Http = "http";
    private const string Silo = "silo";
    private static readonly CompositeFormat OriginFormat = CompositeFormat.Parse(PublicOriginTemplate);
    private const string ClusterPrefix = "keyload-";
    private const string ParameterPrefix = "membership-";
    private const string DataRootEnvironment = "KeyLoad__DataDirectory";
    private const string ClusterEnvironment = "KeyLoad__ClusterId";
    private const string PhysicalEnvironment = "KeyLoad__PhysicalShardId";
    private const string IncarnationEnvironment = "KeyLoad__Incarnation";
    private const string SigningEnvironment = "KeyLoad__SigningKey";
    private const string PeerEnvironment = "KeyLoad__PeerSecret";
    private const string AdminEnvironment = "KeyLoad__AdminKey";
    private const string PublicEnvironment = "KeyLoad__PublicEndpoint";
    private const string SiloAddressEnvironment = "KeyLoad__SiloAddress";
    private const string SiloPortEnvironment = "KeyLoad__SiloPort";
    private const string PrivateHttpEnvironment = "KeyLoad__AllowPrivateNetworkHttp";
    private const string TrustPrefix = "KeyLoad__PhysicalGroupTrust__";
    private const string NativeMembershipReadyPath = "/health/membership-ready";
    private const string True = "true";
    private static readonly string[] Nodes = [TwoRf3ProfileProtocol.FirstVoterName, TwoRf3ProfileProtocol.SecondVoterName, TwoRf3ProfileProtocol.ThirdVoterName, TwoRf3ProfileProtocol.FourthVoterName, TwoRf3ProfileProtocol.FifthVoterName, TwoRf3ProfileProtocol.SixthVoterName];

    internal static IResourceBuilder<ContainerResource>[] Add(IDistributedApplicationBuilder builder,
        LocalProfile profile, string dataRoot)
    {
        const string SecondPhysicalShardParameterSuffix = "physical-b";
        const string SecondIncarnationParameterSuffix = "incarnation-b";

        ValidateMode(builder, profile);
        var registerOwners = TwoRf3Profile.ReadRegistration(builder.Configuration);
        var localImage = LocalDevelopmentContainerImage.Read(builder);
        var image = localImage is null
            ? RuntimeContainerImage.Read(builder, RuntimeContainerImage.ServerConfiguration)
            : null;
        var root = Path.GetFullPath(dataRoot);
        ClusterProfileStore.PrepareDirectory(root);
        var secondPhysical = Guid.NewGuid();
        var secondIncarnation = Guid.NewGuid();
        var secondPeerSecret = RandomSecret();
        var physicalB = builder.AddParameter(ParameterPrefix + SecondPhysicalShardParameterSuffix, secondPhysical.ToString(ParameterIdentityFormat));
        var incarnationB = builder.AddParameter(ParameterPrefix + SecondIncarnationParameterSuffix, secondIncarnation.ToString(ParameterIdentityFormat));
        var signing = builder.AddParameter(SigningKeyParameterName, profile.SigningKey, secret: true);
        var admin = builder.AddParameter(AdminKeyParameterName, profile.AdminKey, secret: true);
        var firstPeer = builder.AddParameter(MembershipPeerParameterName, profile.PeerSecret, secret: true);
        var secondPeer = builder.AddParameter(ParameterPrefix + PeerParameterName, secondPeerSecret, secret: true);
        CryptographicOperations.ZeroMemory(Convert.FromBase64String(secondPeerSecret));
        var containerUser = ClusterContainerUser.Resolve(builder, TimeProvider.System);
        var firstGroup = Nodes[..TwoRf3ProfileProtocol.MembersPerGroup];
        var secondGroup = Nodes[TwoRf3ProfileProtocol.MembersPerGroup..];
        var clusterId = ClusterPrefix + profile.Incarnation.ToString(ResourceIdentityFormat);
        var probe = TwoRf3QueryProbe.Create(builder, root, image, localImage);
        var frameObservation = MovementFrameObservationProfile.Create(builder, root, image, localImage);
        var discoveryOmission = NativeDiscoveryOmissionProfile.Create(builder, root, image, localImage);
        var resources = AddNodes(builder, profile, root, secondPhysical, secondIncarnation, physicalB, incarnationB, signing, admin,
            firstPeer, secondPeer, containerUser, firstGroup, secondGroup, clusterId, image, localImage);
        var tables = NativeClusteringResources.Add(builder);
        foreach (var resource in resources)
        {
            NativeClusteringResources.Apply(resource, tables);
        }
        TwoRf3QueryProbe.Apply(probe, resources, Nodes);
        global::KeyLoad.AppHost.Features.BlobStorage.NativeBlobWireProbeProfile.Apply(builder, root, resources);
        frameObservation?.ApplyReceivers(resources, Nodes);
        discoveryOmission?.Apply(resources);
        TwoRf3RemoteReadResources.Configure(AppHostOptionsRegistration.Get(builder).Control, resources, registerOwners);
        return resources;
    }

    private static IResourceBuilder<ContainerResource>[] AddNodes(IDistributedApplicationBuilder builder,
        LocalProfile profile, string root, Guid secondPhysical, Guid secondIncarnation,
        IResourceBuilder<ParameterResource> physicalB, IResourceBuilder<ParameterResource> incarnationB,
        IResourceBuilder<ParameterResource> signing, IResourceBuilder<ParameterResource> admin,
        IResourceBuilder<ParameterResource> firstPeer, IResourceBuilder<ParameterResource> secondPeer,
        string? containerUser, string[] firstGroup, string[] secondGroup, string clusterId,
        RuntimeContainerImage? image, LocalDevelopmentContainerImage? localImage)
    {
        const int IndexInitialValue = 0;
        const string ContainerNamePrefix = "keyload-";
        const string ContainerNameSeparator = "-";
        const string SiloTransportScheme = "tcp";

        var resources = new IResourceBuilder<ContainerResource>[TwoRf3ProfileProtocol.TotalNodes];
        for (var index = IndexInitialValue; index < Nodes.Length; index++)
        {
            var groupA = index < TwoRf3ProfileProtocol.MembersPerGroup;
            var group = groupA ? firstGroup : secondGroup;
            var physical = groupA ? profile.PhysicalShardId : secondPhysical;
            var incarnation = groupA ? profile.Incarnation : secondIncarnation;
            var peerSecret = groupA ? firstPeer : secondPeer;
            var name = Nodes[index];
            var directory = Path.Combine(root, name);
            ClusterProfileStore.PrepareDirectory(directory);
            var resourceBuilder = image is null
                ? builder.AddContainer(name, LocalDevelopmentContainerImage.Repository,
                    (localImage ?? throw new InvalidOperationException(TwoRf3ProfileProtocol.Invalid)).Tag)
                : image.Add(builder, name);
            var resource = resourceBuilder
                .WithContainerName(ContainerNamePrefix + incarnation.ToString(ResourceIdentityFormat) + ContainerNameSeparator + name)
                .WithContainerNetworkAlias(name)
                .WithBindMount(directory, Data)
                .WithHttpEndpoint(targetPort: TwoRf3ProfileProtocol.HttpPort, name: Http, isProxied: false)
                .WithEndpoint(targetPort: TwoRf3ProfileProtocol.SiloPort, name: Silo, scheme: SiloTransportScheme,
                    isExternal: false, isProxied: false)
                .WithEnvironment(DataRootEnvironment, Data)
                .WithEnvironment(ClusterEnvironment, clusterId);
            ApplyPhysicalIdentity(resource, groupA, physical, incarnation, physicalB, incarnationB);
            resource
                .WithEnvironment(SigningEnvironment, signing).WithEnvironment(PeerEnvironment, peerSecret)
                .WithEnvironment(AdminEnvironment, admin).WithEnvironment(PrivateHttpEnvironment, True)
                .WithEnvironment(PublicEnvironment, Origin(name)).WithEnvironment(SiloAddressEnvironment, name)
                .WithEnvironment(SiloPortEnvironment, TwoRf3ProfileProtocol.SiloPort.ToString(CultureInfo.InvariantCulture))
                .WithHttpHealthCheck(NativeMembershipReadyPath, endpointName: Http);
            TwoRf3TopologyResources.ApplyPeerEndpoints(resource, group);
            ApplyPhysicalTrustSettings(resource, groupA, profile, physicalB, incarnationB,
                firstPeer, secondPeer, firstGroup, secondGroup);
            KeyLoad.AppHost.Features.ClientApi.ClusterConnectionEndpointResources.Apply(resource);
            ClusterResourceSettings.Apply(builder, resource, containerUser);
            resources[index] = resource;
        }
        return resources;
    }

    private static void ApplyPhysicalIdentity(IResourceBuilder<ContainerResource> resource, bool groupA,
        Guid physical, Guid incarnation, IResourceBuilder<ParameterResource> physicalB,
        IResourceBuilder<ParameterResource> incarnationB)
    {
        if (groupA)
        {
            resource.WithEnvironment(PhysicalEnvironment, physical.ToString(ParameterIdentityFormat))
                .WithEnvironment(IncarnationEnvironment, incarnation.ToString(ParameterIdentityFormat));
            return;
        }
        resource.WithEnvironment(PhysicalEnvironment, physicalB).WithEnvironment(IncarnationEnvironment, incarnationB);
    }

    private static void ApplyPhysicalTrustSettings(IResourceBuilder<ContainerResource> resource, bool groupA,
        LocalProfile profile, IResourceBuilder<ParameterResource> secondPhysical,
        IResourceBuilder<ParameterResource> secondIncarnation, IResourceBuilder<ParameterResource> firstSecret,
        IResourceBuilder<ParameterResource> secondSecret, string[] firstGroup, string[] secondGroup)
    {
        resource.WithEnvironment(TrustPrefix + RoleEnvironment, groupA ? ControlRole : DestinationRole);
        if (groupA)
        {
            resource.WithEnvironment(TrustPrefix + ForeignPhysicalEnvironment, secondPhysical)
                .WithEnvironment(TrustPrefix + ForeignIncarnationEnvironment, secondIncarnation)
                .WithEnvironment(TrustPrefix + ForeignSecretEnvironment, secondSecret);
        }
        else
        {
            resource.WithEnvironment(TrustPrefix + ForeignPhysicalEnvironment, profile.PhysicalShardId.ToString(ParameterIdentityFormat))
                .WithEnvironment(TrustPrefix + ForeignIncarnationEnvironment, profile.Incarnation.ToString(ParameterIdentityFormat))
                .WithEnvironment(TrustPrefix + ForeignSecretEnvironment, firstSecret);
        }
        AddVector(resource, ForeignVotersEnvironment, (groupA ? secondGroup : firstGroup).Select(Origin).ToArray());
    }

    private static void AddVector(IResourceBuilder<ContainerResource> resource, string name, string[] values)
    {
        const int IndexInitialValue = 0;
        const string EnvironmentIndexSeparator = "__";
        for (var index = IndexInitialValue; index < values.Length; index++)
        {
            resource.WithEnvironment(TrustPrefix + name + EnvironmentIndexSeparator + index.ToString(CultureInfo.InvariantCulture), values[index]);
        }
    }

    private static void ValidateMode(IDistributedApplicationBuilder builder, LocalProfile profile)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ClusterProfileStore.Validate(profile);
        if (!AppHostOptionsRegistration.Get(builder).Control.Value.Ephemeral
            || AppHostOptionsRegistration.Get(builder).Control.Value.BenchmarksEnabled
            || AppHostOptionsRegistration.Get(builder).Control.Value.ProtocolCohortEnabled)
        {
            throw new InvalidOperationException(TwoRf3ProfileProtocol.Invalid);
        }
    }

    internal static string Origin(string node) => string.Format(CultureInfo.InvariantCulture, OriginFormat, node);

    private static string RandomSecret()
    {
        const int CountValue = 32;

        var bytes = RandomNumberGenerator.GetBytes(CountValue);
        try
        {
            return Convert.ToBase64String(bytes);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
