using System.Globalization;
using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TwoRf3MembershipFingerprintOracle
{
    private const string PhysicalParameter = "membership-physical-b";
    private const string IncarnationParameter = "membership-incarnation-b";
    private const string SecretParameter = "membership-peer-b";
    private const string ClusterPrefix = "keyload-";

    internal static async Task VerifyAsync(DistributedApplication app, NodeEpochRf3Profile profile,
        CancellationToken cancellationToken)
    {
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var identity = await ReadIdentityAsync(model, profile, cancellationToken).ConfigureAwait(false);
        byte[]? firstSecret = null;
        byte[]? secondSecret = null;
        try
        {
            firstSecret = Convert.FromBase64String(profile.PeerSecret);
            secondSecret = Convert.FromBase64String(identity.Secret);
            var observations = await ReadSixAsync(app, profile, identity, firstSecret, secondSecret,
                cancellationToken).ConfigureAwait(false);
            var clusterId = ClusterPrefix + profile.Incarnation.ToString("N", CultureInfo.InvariantCulture);
            await VerifyWrongContextsAsync(app, identity, clusterId, secondSecret, cancellationToken)
                .ConfigureAwait(false);
            var expected = TwoRf3MembershipFingerprintAssertions.Fingerprint(observations.Select(value => value.SiloAddress));
            await TwoRf3MembershipFingerprintAssertions.VerifyHealthAsync(app, expected, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (firstSecret is not null)
            { CryptographicOperations.ZeroMemory(firstSecret); }
            if (secondSecret is not null)
            { CryptographicOperations.ZeroMemory(secondSecret); }
        }
    }

    private static async Task<TwoRf3MembershipIdentity> ReadIdentityAsync(DistributedApplicationModel model,
        NodeEpochRf3Profile profile, CancellationToken token)
    {
        var physical = await ReadParameterAsync(model, PhysicalParameter, token).ConfigureAwait(false);
        var incarnation = await ReadParameterAsync(model, IncarnationParameter, token).ConfigureAwait(false);
        var secret = await ReadParameterAsync(model, SecretParameter, token).ConfigureAwait(false);
        if (!Guid.TryParseExact(physical, "D", out var physicalId) || physicalId == Guid.Empty
            || physicalId.ToString("D") != physical
            || physicalId == profile.PhysicalShardId || !Guid.TryParseExact(incarnation, "D", out var incarnationId)
            || incarnationId == Guid.Empty || incarnationId.ToString("D") != incarnation
            || incarnationId == profile.Incarnation || secret.Length == 0)
        { throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState); }
        return new(incarnationId, secret);
    }

    private static async Task<string> ReadParameterAsync(DistributedApplicationModel model, string name,
        CancellationToken token)
    {
        var parameter = model.Resources.OfType<ParameterResource>().Single(resource => resource.Name == name);
        return await parameter.GetValueAsync(token).ConfigureAwait(false)
            ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
    }

    private static async Task<ReplicaSiloDiscovery[]> ReadSixAsync(DistributedApplication app,
        NodeEpochRf3Profile profile, TwoRf3MembershipIdentity identity, byte[] firstSecret, byte[] secondSecret,
        CancellationToken token)
    {
        var observations = new ReplicaSiloDiscovery[TwoRf3MembershipProtocol.NodeCount];
        var clusterId = ClusterPrefix + profile.Incarnation.ToString("N", CultureInfo.InvariantCulture);
        for (var index = 0; index < observations.Length; index++)
        {
            var firstGroup = index < TwoRf3MembershipProtocol.MembersPerGroup;
            var secret = firstGroup ? firstSecret : secondSecret;
            var incarnation = firstGroup ? profile.Incarnation : identity.Incarnation;
            observations[index] = await TwoRf3MembershipSignedDiscovery.ReadAsync(app,
                TwoRf3MembershipProtocol.Nodes[index], clusterId, incarnation, secret, token).ConfigureAwait(false);
        }
        return observations;
    }

    private static async Task VerifyWrongContextsAsync(DistributedApplication app, TwoRf3MembershipIdentity identity,
        string clusterId, byte[] secret, CancellationToken token)
    {
        var wrongSecret = secret.ToArray();
        wrongSecret[0] ^= 1;
        try
        {
            await RejectAsync(app, clusterId + "-wrong", identity.Incarnation, secret, token).ConfigureAwait(false);
            await RejectAsync(app, clusterId, Guid.NewGuid(), secret, token).ConfigureAwait(false);
            await RejectAsync(app, clusterId, identity.Incarnation, wrongSecret, token).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(wrongSecret); }
    }

    private static async Task RejectAsync(DistributedApplication app, string clusterId, Guid incarnation,
        byte[] secret, CancellationToken token)
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TwoRf3MembershipSignedDiscovery.ReadAsync(app, TwoRf3MembershipProtocol.Node4,
                clusterId, incarnation, secret, token));
        await Assert.That(failure).IsNotNull();
    }

}
