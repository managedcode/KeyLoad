using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using KeyLoad.AppHost.Features.ClusterReplication;

namespace KeyLoad.AppHost.Features.ClusterRouting;

internal static class TwoRf3ClusterResources
{
    private const string ParameterIdentityFormat = "D";
    private const string ResourceIdentityFormat = "N";

    private const string Data = "/data";
    private const string Http = "http";
    private const string Silo = "silo";
    private static readonly CompositeFormat OriginFormat = CompositeFormat.Parse("http://{0}:8080");
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
    private const string PeerPrefix = "KeyLoad__Peers__";
    private const string AuthorityPrefix = "KeyLoad__MembershipAuthority__";
    private const string AuthorityHealth = "/health/membership-authority";
    private const string MembershipHealth = "/health/membership-ready";
    private const string True = "true";
    private static readonly string[] Nodes = ["node1", "node2", "node3", "node4", "node5", "node6"];

    internal static IResourceBuilder<ContainerResource>[] Add(IDistributedApplicationBuilder builder,
        LocalProfile profile, string dataRoot)
    {
        ValidateMode(builder, profile);
        var root = Path.GetFullPath(dataRoot);
        ClusterProfileStore.PrepareDirectory(root);
        var image = RuntimeContainerImage.Read(builder, RuntimeContainerImage.ServerConfiguration);
        var secondPhysical = Guid.NewGuid();
        var secondIncarnation = Guid.NewGuid();
        var secondPeerSecret = RandomSecret();
        var physicalB = builder.AddParameter(ParameterPrefix + "physical-b", secondPhysical.ToString(ParameterIdentityFormat));
        var incarnationB = builder.AddParameter(ParameterPrefix + "incarnation-b", secondIncarnation.ToString(ParameterIdentityFormat));
        var signing = builder.AddParameter("signing-key", profile.SigningKey, secret: true);
        var admin = builder.AddParameter("admin-key", profile.AdminKey, secret: true);
        var firstPeer = builder.AddParameter("membership-peer-a", profile.PeerSecret, secret: true);
        var secondPeer = builder.AddParameter(ParameterPrefix + "peer-b", secondPeerSecret, secret: true);
        CryptographicOperations.ZeroMemory(Convert.FromBase64String(secondPeerSecret));
        var containerUser = ClusterContainerUser.Resolve(builder);
        var firstGroup = Nodes[..TwoRf3ProfileProtocol.MembersPerGroup];
        var secondGroup = Nodes[TwoRf3ProfileProtocol.MembersPerGroup..];
        var clusterId = ClusterPrefix + profile.Incarnation.ToString(ResourceIdentityFormat);
        return AddNodes(builder, profile, root, image, secondPhysical, secondIncarnation, physicalB, incarnationB, signing, admin,
            firstPeer, secondPeer, containerUser, firstGroup, secondGroup, clusterId);
    }

    private static IResourceBuilder<ContainerResource>[] AddNodes(IDistributedApplicationBuilder builder,
        LocalProfile profile, string root, RuntimeContainerImage image, Guid secondPhysical, Guid secondIncarnation,
        IResourceBuilder<ParameterResource> physicalB, IResourceBuilder<ParameterResource> incarnationB,
        IResourceBuilder<ParameterResource> signing, IResourceBuilder<ParameterResource> admin,
        IResourceBuilder<ParameterResource> firstPeer, IResourceBuilder<ParameterResource> secondPeer,
        string? containerUser, string[] firstGroup, string[] secondGroup, string clusterId)
    {
        var resources = new IResourceBuilder<ContainerResource>[TwoRf3ProfileProtocol.TotalNodes];
        for (var index = 0; index < Nodes.Length; index++)
        {
            var groupA = index < TwoRf3ProfileProtocol.MembersPerGroup;
            var group = groupA ? firstGroup : secondGroup;
            var physical = groupA ? profile.PhysicalShardId : secondPhysical;
            var incarnation = groupA ? profile.Incarnation : secondIncarnation;
            var peerSecret = groupA ? firstPeer : secondPeer;
            var name = Nodes[index];
            var directory = Path.Combine(root, name);
            ClusterProfileStore.PrepareDirectory(directory);
            var resource = image.Add(builder, name)
                .WithContainerName("keyload-" + incarnation.ToString(ResourceIdentityFormat) + "-" + name)
                .WithContainerNetworkAlias(name)
                .WithBindMount(directory, Data)
                .WithHttpEndpoint(targetPort: TwoRf3ProfileProtocol.HttpPort, name: Http, isProxied: false)
                .WithEndpoint(targetPort: TwoRf3ProfileProtocol.SiloPort, name: Silo, scheme: "tcp",
                    isExternal: false, isProxied: false)
                .WithEnvironment(DataRootEnvironment, Data)
                .WithEnvironment(ClusterEnvironment, clusterId);
            if (groupA)
            {
                resource.WithEnvironment(PhysicalEnvironment, physical.ToString(ParameterIdentityFormat))
                .WithEnvironment(IncarnationEnvironment, incarnation.ToString(ParameterIdentityFormat));
            }
            else
            { resource.WithEnvironment(PhysicalEnvironment, physicalB).WithEnvironment(IncarnationEnvironment, incarnationB); }
            resource
                .WithEnvironment(SigningEnvironment, signing).WithEnvironment(PeerEnvironment, peerSecret)
                .WithEnvironment(AdminEnvironment, admin).WithEnvironment(PrivateHttpEnvironment, True)
                .WithEnvironment(PublicEnvironment, Origin(name)).WithEnvironment(SiloAddressEnvironment, name)
                .WithEnvironment(SiloPortEnvironment, TwoRf3ProfileProtocol.SiloPort.ToString(CultureInfo.InvariantCulture))
                .WithHttpHealthCheck(groupA ? AuthorityHealth : MembershipHealth, endpointName: Http);
            for (var peerIndex = 0; peerIndex < group.Length; peerIndex++)
            { resource.WithEnvironment(PeerPrefix + peerIndex.ToString(CultureInfo.InvariantCulture), Origin(group[peerIndex])); }
            ApplyAuthoritySettings(resource, groupA, profile, physicalB, incarnationB,
                firstPeer, secondPeer, firstGroup, secondGroup);
            ClusterResourceSettings.Apply(builder, resource, containerUser);
            if (!groupA)
            {
                for (var authorityIndex = 0; authorityIndex < firstGroup.Length; authorityIndex++)
                { resource.WaitFor(resources[authorityIndex]); }
            }
            resources[index] = resource;
        }
        return resources;
    }

    private static void ApplyAuthoritySettings(IResourceBuilder<ContainerResource> resource, bool groupA,
        LocalProfile profile, IResourceBuilder<ParameterResource> secondPhysical,
        IResourceBuilder<ParameterResource> secondIncarnation, IResourceBuilder<ParameterResource> firstSecret,
        IResourceBuilder<ParameterResource> secondSecret,
        string[] firstGroup, string[] secondGroup)
    {
        resource.WithEnvironment(AuthorityPrefix + "Mode", groupA ? "authority" : "proxy");
        if (groupA)
        {
            resource.WithEnvironment(AuthorityPrefix + "TrustedGroup__PhysicalShardId", secondPhysical)
                .WithEnvironment(AuthorityPrefix + "TrustedGroup__Incarnation", secondIncarnation)
                .WithEnvironment(AuthorityPrefix + "TrustedGroup__PeerSecret", secondSecret);
            AddVector(resource, "TrustedGroup__VoterIds", secondGroup.Select(Origin).ToArray());
            AddVector(resource, "TrustedGroup__SiloEndpoints", secondGroup.Select(name => name + ":11111").ToArray());
        }
        else
        {
            resource.WithEnvironment(AuthorityPrefix + "AuthorityPhysicalShardId", profile.PhysicalShardId.ToString(ParameterIdentityFormat))
                .WithEnvironment(AuthorityPrefix + "AuthorityIncarnation", profile.Incarnation.ToString(ParameterIdentityFormat))
                .WithEnvironment(AuthorityPrefix + "AuthorityPeerSecret", firstSecret);
            AddVector(resource, "AuthorityEndpoints", firstGroup.Select(Origin).ToArray());
        }
    }

    private static void AddVector(IResourceBuilder<ContainerResource> resource, string name, string[] values)
    {
        for (var index = 0; index < values.Length; index++)
        { resource.WithEnvironment(AuthorityPrefix + name + "__" + index.ToString(CultureInfo.InvariantCulture), values[index]); }
    }

    private static void ValidateMode(IDistributedApplicationBuilder builder, LocalProfile profile)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ClusterProfileStore.Validate(profile);
        if (!builder.Configuration.GetValue<bool>(TwoRf3ProfileProtocol.EphemeralSetting)
            || builder.Configuration.GetValue<bool>(TwoRf3ProfileProtocol.BenchmarksEnabledSetting)
            || LocalDevelopmentContainerImage.Read(builder) is not null
            || builder.Configuration.GetValue<bool>(ProtocolCohortImages.EnabledSetting)
            || RequestCqrsProbeProfileSettingsReader.Read(builder.Configuration) is not null)
        { throw new InvalidOperationException(TwoRf3ProfileProtocol.Invalid); }
    }

    private static string Origin(string node) => string.Format(CultureInfo.InvariantCulture, OriginFormat, node);

    private static string RandomSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        try
        { return Convert.ToBase64String(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
