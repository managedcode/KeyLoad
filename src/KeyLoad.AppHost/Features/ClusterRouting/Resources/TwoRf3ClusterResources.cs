using KeyLoad.AppHost.Hosting;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using KeyLoad.AppHost.Features.ClusterReplication;

namespace KeyLoad.AppHost.Features.ClusterRouting;

internal static class TwoRf3ClusterResources
{
    private const string ThirdVoterName = "node3";
    private const string FourthVoterName = "node4";
    private const string FifthVoterName = "node5";
    private const string SixthVoterName = "node6";
    private const string SigningKeyParameterName = "signing-key";
    private const string AdminKeyParameterName = "admin-key";
    private const string MembershipPeerParameterName = "membership-peer-a";
    private const string PeerParameterName = "peer-b";
    private const string TrustedGroupIncarnationEnvironment = "TrustedGroup__Incarnation";
    private const string TrustedGroupPeerSecretEnvironment = "TrustedGroup__PeerSecret";
    private const string TrustedGroupVotersEnvironment = "TrustedGroup__VoterIds";
    private const string TrustedGroupSiloEndpointsEnvironment = "TrustedGroup__SiloEndpoints";
    private const string AuthorityPhysicalShardEnvironment = "AuthorityPhysicalShardId";
    private const string AuthorityIncarnationEnvironment = "AuthorityIncarnation";
    private const string AuthorityPeerSecretEnvironment = "AuthorityPeerSecret";
    private const string AuthorityEndpointsEnvironment = "AuthorityEndpoints";

    private const string OriginFormatFormatText = "http://{0}:8080";
    private const string NodesResultText = "node1";
    private const string NodesNodesResultText = "node2";

    private const string ParameterIdentityFormat = "D";
    private const string ResourceIdentityFormat = "N";

    private const string Data = "/data";
    private const string Http = "http";
    private const string Silo = "silo";
    private static readonly CompositeFormat OriginFormat = CompositeFormat.Parse(OriginFormatFormatText);
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
    private static readonly string[] Nodes = [NodesResultText, NodesNodesResultText, ThirdVoterName, FourthVoterName, FifthVoterName, SixthVoterName];

    internal static IResourceBuilder<ContainerResource>[] Add(IDistributedApplicationBuilder builder,
        LocalProfile profile, string dataRoot)
    {
        const string NameText = "physical-b";
        const string AddNameText = "incarnation-b";

        ValidateMode(builder, profile);
        var root = Path.GetFullPath(dataRoot);
        ClusterProfileStore.PrepareDirectory(root);
        var image = RuntimeContainerImage.Read(builder, RuntimeContainerImage.ServerConfiguration);
        var secondPhysical = Guid.NewGuid();
        var secondIncarnation = Guid.NewGuid();
        var secondPeerSecret = RandomSecret();
        var physicalB = builder.AddParameter(ParameterPrefix + NameText, secondPhysical.ToString(ParameterIdentityFormat));
        var incarnationB = builder.AddParameter(ParameterPrefix + AddNameText, secondIncarnation.ToString(ParameterIdentityFormat));
        var signing = builder.AddParameter(SigningKeyParameterName, profile.SigningKey, secret: true);
        var admin = builder.AddParameter(AdminKeyParameterName, profile.AdminKey, secret: true);
        var firstPeer = builder.AddParameter(MembershipPeerParameterName, profile.PeerSecret, secret: true);
        var secondPeer = builder.AddParameter(ParameterPrefix + PeerParameterName, secondPeerSecret, secret: true);
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
        const int IndexInitialValue = 0;
        const string NameText = "keyload-";
        const string AddNodesNameText = "-";
        const string SchemeText = "tcp";
        const int PeerIndexInitialValue = 0;
        const int AuthorityIndexInitialValue = 0;

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
            var resource = image.Add(builder, name)
                .WithContainerName(NameText + incarnation.ToString(ResourceIdentityFormat) + AddNodesNameText + name)
                .WithContainerNetworkAlias(name)
                .WithBindMount(directory, Data)
                .WithHttpEndpoint(targetPort: TwoRf3ProfileProtocol.HttpPort, name: Http, isProxied: false)
                .WithEndpoint(targetPort: TwoRf3ProfileProtocol.SiloPort, name: Silo, scheme: SchemeText,
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
            for (var peerIndex = PeerIndexInitialValue; peerIndex < group.Length; peerIndex++)
            { resource.WithEnvironment(PeerPrefix + peerIndex.ToString(CultureInfo.InvariantCulture), Origin(group[peerIndex])); }
            ApplyAuthoritySettings(resource, groupA, profile, physicalB, incarnationB,
                firstPeer, secondPeer, firstGroup, secondGroup);
            ClusterResourceSettings.Apply(builder, resource, containerUser);
            if (!groupA)
            {
                for (var authorityIndex = AuthorityIndexInitialValue; authorityIndex < firstGroup.Length; authorityIndex++)
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
        const string NameText = "Mode";
        const string ValueText = "authority";
        const string ApplyAuthoritySettingsValueText = "proxy";
        const string ApplyAuthoritySettingsNameText = "TrustedGroup__PhysicalShardId";
        const string SelectorText = ":11111";

        resource.WithEnvironment(AuthorityPrefix + NameText, groupA ? ValueText : ApplyAuthoritySettingsValueText);
        if (groupA)
        {
            resource.WithEnvironment(AuthorityPrefix + ApplyAuthoritySettingsNameText, secondPhysical)
                .WithEnvironment(AuthorityPrefix + TrustedGroupIncarnationEnvironment, secondIncarnation)
                .WithEnvironment(AuthorityPrefix + TrustedGroupPeerSecretEnvironment, secondSecret);
            AddVector(resource, TrustedGroupVotersEnvironment, secondGroup.Select(Origin).ToArray());
            AddVector(resource, TrustedGroupSiloEndpointsEnvironment, secondGroup.Select(name => name + SelectorText).ToArray());
        }
        else
        {
            resource.WithEnvironment(AuthorityPrefix + AuthorityPhysicalShardEnvironment, profile.PhysicalShardId.ToString(ParameterIdentityFormat))
                .WithEnvironment(AuthorityPrefix + AuthorityIncarnationEnvironment, profile.Incarnation.ToString(ParameterIdentityFormat))
                .WithEnvironment(AuthorityPrefix + AuthorityPeerSecretEnvironment, firstSecret);
            AddVector(resource, AuthorityEndpointsEnvironment, firstGroup.Select(Origin).ToArray());
        }
    }

    private static void AddVector(IResourceBuilder<ContainerResource> resource, string name, string[] values)
    {
        const int IndexInitialValue = 0;
        const string NameText = "__";

        for (var index = IndexInitialValue; index < values.Length; index++)
        { resource.WithEnvironment(AuthorityPrefix + name + NameText + index.ToString(CultureInfo.InvariantCulture), values[index]); }
    }

    private static void ValidateMode(IDistributedApplicationBuilder builder, LocalProfile profile)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ClusterProfileStore.Validate(profile);
        if (!AppHostOptionsRegistration.Get(builder).Control.Value.Ephemeral
            || AppHostOptionsRegistration.Get(builder).Control.Value.BenchmarksEnabled
            || LocalDevelopmentContainerImage.Read(builder) is not null
            || AppHostOptionsRegistration.Get(builder).Control.Value.ProtocolCohortEnabled
            || AppHostOptionsRegistration.Get(builder).Control.Value.RequestProbe is not null)
        { throw new InvalidOperationException(TwoRf3ProfileProtocol.Invalid); }
    }

    private static string Origin(string node) => string.Format(CultureInfo.InvariantCulture, OriginFormat, node);

    private static string RandomSecret()
    {
        const int CountValue = 32;

        var bytes = RandomNumberGenerator.GetBytes(CountValue);
        try
        { return Convert.ToBase64String(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
