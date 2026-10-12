using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Composes validated native admission options from real listener and persisted owner identities.</summary>
[ConfigurationBinding]
internal static class ControlledPartitionMovementLoopbackOptions
{
    private const string ControlCluster = "movement-loopback-control";
    private const string DestinationCluster = "movement-loopback-destination";
    private const string InfrastructureCredential = "root.movement-native-fixture-credential-32-characters";
    private const string EnabledKey = PartitionMovementExecutionOptions.SectionName + ":Enabled";
    private const string EnabledValue = "true";

    internal static ServerRuntimeOptions Bind(ControlledPartitionMovementNode node,
        ControlledPartitionMovementLoopbackCorpus corpus, string controlPeerSecret, string destinationPeerSecret,
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
            PhysicalGroupTrust = Settings(corpus, controlPeerSecret, destinationPeerSecret, control)
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

    private static PhysicalGroupTrustOptions Settings(ControlledPartitionMovementLoopbackCorpus corpus,
        string controlPeerSecret, string destinationPeerSecret, bool control)
        => control ? new()
        {
            Role = PhysicalGroupTrustRole.Control,
            RegisterPhysicalOwners = true,
            ForeignPhysicalShardId = corpus.Destination.Owner.PhysicalShardId,
            ForeignIncarnation = corpus.Destination.Owner.Incarnation,
            ForeignVoterIds = [.. corpus.Destination.Owner.VoterIds],
            ForeignPeerSecret = destinationPeerSecret
        } : new()
        {
            Role = PhysicalGroupTrustRole.Destination,
            RegisterPhysicalOwners = true,
            ForeignPhysicalShardId = corpus.Control.Owner.PhysicalShardId,
            ForeignIncarnation = corpus.Control.Owner.Incarnation,
            ForeignVoterIds = [.. corpus.Control.Owner.VoterIds],
            ForeignPeerSecret = controlPeerSecret
        };
}
