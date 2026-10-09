using System.Globalization;
using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Binds every inspected namespace to its real signed source/target native identity.</summary>
internal sealed class ReplicaIsolationNodeReader(ReplicaIsolationBuildPlan plan,
    IReadOnlyDictionary<string, string> names, string root, TwoRf3MembershipWave? six)
{
    private const string DashedGuid = "D";
    private const string CompactGuid = "N";
    private const int SiloPort = 11111;
    private const string TargetIncarnation = "membership-incarnation-b";
    private const string TargetSecret = "membership-peer-b";
    private const string ClusterPrefix = "keyload-";
    internal async Task<ReplicaIsolationNode> ReadAsync(DistributedApplication app,
        ReplicaIsolationBuildTarget target, CancellationToken cancellationToken)
    {
        var container = await ReplicaIsolationInspectionReader.ContainerAsync(names[target.ResourceName], plan, target, cancellationToken);
        _ = ReplicaIsolationRules.RequireIpv4(container.IpAddress);
        var discovery = six is null
            ? await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(app, target.ResourceName,
                new NodeEpochRf3Profile(ClusterFixturePhysicalShardIdentity.ReadProfile(root)), cancellationToken)
            : await ReadSixAsync(app, target.ResourceName, cancellationToken);
        var endpoint = SiloAddress.FromParsableString(discovery.SiloAddress).Endpoint;
        if (!discovery.TransportReady || endpoint.Port != SiloPort || endpoint.Address.ToString() != container.IpAddress)
        { throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState); }
        return new(target.ResourceName, container, discovery.SiloAddress);
    }

    private async Task<KeyLoad.Orleans.ReplicaSiloDiscovery> ReadSixAsync(DistributedApplication app,
        string resource, CancellationToken cancellationToken)
    {
        var profile = six!.Profile;
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var source = RequestCqrsProbeFixtureProtocol.Nodes.Contains(resource, StringComparer.Ordinal);
        var incarnationText = source ? profile.Incarnation.ToString(DashedGuid) : await model.Resources.OfType<ParameterResource>()
            .Single(value => value.Name == TargetIncarnation).GetValueAsync(cancellationToken);
        if (!Guid.TryParseExact(incarnationText, DashedGuid, out var incarnation) || incarnation == Guid.Empty
            || !source && incarnation == profile.Incarnation)
        { throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState); }
        var secretText = source ? profile.PeerSecret : await model.Resources.OfType<ParameterResource>()
            .Single(value => value.Name == TargetSecret).GetValueAsync(cancellationToken);
        var secret = Convert.FromBase64String(secretText ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState));
        try
        {
            return await TwoRf3MembershipSignedDiscovery.ReadAsync(app, resource,
            ClusterPrefix + profile.Incarnation.ToString(CompactGuid, CultureInfo.InvariantCulture), incarnation, secret, cancellationToken);
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }
}
