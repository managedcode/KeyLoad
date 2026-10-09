using System.Security.Cryptography;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Validated fixed trust identities for a supporting native signed-HTTP fixture, not an RF3 topology.</summary>
[ConfigurationBinding]
internal static class ReplicaMembershipAuthorityHeartbeatOptions
{
    private const string Cluster = "membership-native-unit";
    private const string Administrator = "root.membership-heartbeat-fixture-credential-000000000000";
    private const string Authority1 = "http://127.0.0.4:8080";
    private const string Authority2 = "http://127.0.0.5:8080";
    private const string Authority3 = "http://127.0.0.6:8080";
    private const string Caller1 = "http://127.0.0.1:8080";
    private const string Caller2 = "http://127.0.0.2:8080";
    private const string Caller3 = "http://127.0.0.3:8080";
    private const string Silo1 = "127.0.0.1:11111";
    private const string Silo2 = "127.0.0.2:11111";
    private const string Silo3 = "127.0.0.3:11111";
    private const string AuthorityAddress = "127.0.0.4";

    internal static IOptions<NodeOptions> Node(TestDatabase fixture)
    {
        var node = new NodeOptions
        {
            DataDirectory = fixture.Directory,
            PublicEndpoint = Authority1,
            Peers = [Authority1, Authority2, Authority3],
            ClusterId = Cluster,
            PhysicalShardId = Guid.NewGuid(),
            Incarnation = fixture.Store.Identity.Incarnation,
            SigningKey = Convert.ToBase64String(fixture.Store.Identity.SigningKey.Span),
            PeerSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(MembershipAuthoritySettingsProtocol.SecretBytes)),
            AdminKey = Administrator,
            SiloAddress = AuthorityAddress,
            AllowLoopbackHttp = true,
            AllowPrivateNetworkHttp = true,
            MembershipAuthority = new()
            {
                Mode = MembershipAuthoritySettingsProtocol.Authority,
                TrustedGroupPhysicalShardId = Guid.NewGuid(),
                TrustedGroupIncarnation = Guid.NewGuid(),
                TrustedGroupVoterIds = [Caller1, Caller2, Caller3],
                TrustedGroupSiloEndpoints = [Silo1, Silo2, Silo3],
                TrustedGroupPeerSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(MembershipAuthoritySettingsProtocol.SecretBytes))
            }
        };
        node.Validate();
        return Options.Create(node);
    }

    internal static ReplicaMembershipAuthorityExchangeOptions Exchange(NodeOptions node, Uri listener)
        => new(node.ClusterId, node.PhysicalShardId, node.Incarnation,
            node.MembershipAuthority.TrustedGroupPhysicalShardId, node.MembershipAuthority.TrustedGroupIncarnation,
            Caller1, ReplicaMembershipNativeTests.Entry().SiloAddress.ToParsableString(), [listener, listener, listener],
            Convert.FromBase64String(node.MembershipAuthority.TrustedGroupPeerSecret!), Convert.FromBase64String(node.PeerSecret),
            TimeProvider.System);
}
