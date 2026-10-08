using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Composes validated native admission options from real listener and persisted owner identities.</summary>
[ConfigurationBinding]
internal static class ControlledPartitionMovementProcessOptions
{
    private const string ControlCluster = "movement-loopback-control";
    private const string DestinationCluster = "movement-loopback-destination";
    private const string InfrastructureCredential = "root.movement-native-fixture-credential-32-characters";
    private const string EnabledKey = PartitionMovementExecutionOptions.SectionName + ":Enabled";
    private const string EnabledValue = "true";

    internal static ServerRuntimeOptions Bind(ControlledPartitionMovementNativeNode node,
        ControlledPartitionMovementProcessOwners corpus, string controlPeerSecret, string destinationPeerSecret,
        bool control)
    {
        var local = control ? corpus.Control.Owner : corpus.Destination.Owner;
        var options = new NodeOptions
        {
            DataDirectory = node.Root,
            PublicEndpoint = local.VoterIds.First(),
            Peers = local.VoterIds,
            ClusterId = control ? ControlCluster : DestinationCluster,
            PhysicalShardId = local.PhysicalShardId,
            Incarnation = local.Incarnation,
            SigningKey = Convert.ToBase64String(node.Store.Identity.SigningKey.Span),
            PeerSecret = control ? controlPeerSecret : destinationPeerSecret,
            AdminKey = InfrastructureCredential,
            AllowLoopbackHttp = true,
            AllowPrivateNetworkHttp = true,
            SiloAddress = new Uri(local.VoterIds.First()).DnsSafeHost,
            MembershipAuthority = Settings(corpus, controlPeerSecret, destinationPeerSecret, control)
        };
        options.Validate();
        var services = new ServiceCollection();
        services.AddRuntimeOptions(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [EnabledKey] = EnabledValue }).Build());
        services.AddSingleton(Options.Create(options));
        services.AddSingleton(Options.Create(node.Journal.Configuration));
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<ServerRuntimeOptions>();
        runtime.ValidateBeforePhysicalOwnership();
        return runtime;
    }

    private static MembershipAuthoritySettings Settings(ControlledPartitionMovementProcessOwners corpus,
        string controlPeerSecret, string destinationPeerSecret, bool control)
        => control ? new()
        {
            Mode = MembershipAuthoritySettingsProtocol.Authority,
            RegisterPhysicalOwners = true,
            TrustedGroupPhysicalShardId = corpus.Destination.Owner.PhysicalShardId,
            TrustedGroupIncarnation = corpus.Destination.Owner.Incarnation,
            TrustedGroupVoterIds = [.. corpus.Destination.Owner.VoterIds],
            TrustedGroupSiloEndpoints = corpus.DestinationSiloEndpoints,
            TrustedGroupPeerSecret = destinationPeerSecret
        } : new()
        {
            Mode = MembershipAuthoritySettingsProtocol.Proxy,
            RegisterPhysicalOwners = true,
            AuthorityPhysicalShardId = corpus.Control.Owner.PhysicalShardId,
            AuthorityIncarnation = corpus.Control.Owner.Incarnation,
            AuthorityEndpoints = [.. corpus.Control.Owner.VoterIds],
            AuthorityPeerSecret = controlPeerSecret
        };
}
