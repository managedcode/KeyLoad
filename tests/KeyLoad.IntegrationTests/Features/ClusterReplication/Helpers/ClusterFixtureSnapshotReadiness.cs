using System.Security.Cryptography;
using System.Text;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>The one keyed original case owns private controls before AppHost construction and persisted caller setup.</summary>
internal sealed class ClusterFixtureSnapshotReadiness(RequestCqrsProbeFixture controls)
{
    internal const string FixtureKey = "kl035-native-snapshot-readiness";
    internal const string PrincipalId = "c1-probe-kl035-snapshot-admin";
    private const string Tenant = "system";
    private const string KeyPrefix = "snapshot-admin-key-";
    private const string Separator = ".";
    private const int SecretBytes = 32;
    private const long PolicyEpoch = 1;
    private const string EnabledArgument = "--KeyLoadTests:RequestCqrsProbe:Enabled=true";
    private const string RootArgument = "--KeyLoadTests:RequestCqrsProbe:Root=";
    private const string SessionArgument = "--KeyLoadTests:RequestCqrsProbe:SessionId=";
    private string? secret;

    internal RequestCqrsProbeFixture Controls => controls;
    internal string Secret => secret ?? throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.InvalidArm);

    internal static ClusterFixtureSnapshotReadiness? Create(string? key, string root)
        => key == FixtureKey ? new(RequestCqrsProbeFixture.Create(root, Guid.NewGuid())) : null;

    internal string[] Apply(string[] arguments)
        => [.. arguments, EnabledArgument, RootArgument + controls.Root, SessionArgument + controls.SessionId];

    internal async Task ProvisionAsync(ClusterFixture fixture, CancellationToken token)
    {
        if (secret is not null)
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.InvalidArm); }
        var key = KeyPrefix + Guid.NewGuid().ToString("N");
        var credential = key + Separator + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));
        var principal = new PrincipalRecord(PrincipalId, Tenant, [], [])
        { ClusterAdministrator = true, PolicyEpoch = PolicyEpoch };
        var definition = new ApiKeyRecord(key, PrincipalId,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(credential))));
        var actual = ClusterReplicationTestSupport.Success(await fixture.Client(RequestCqrsRf3Protocol.Node1)
            .ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        await Assert.That(actual.Id).IsEqualTo(PrincipalId);
        await Assert.That(actual.ClusterAdministrator).IsTrue();
        await Assert.That(actual.PolicyEpoch).IsEqualTo(PolicyEpoch);
        await Assert.That(ClusterReplicationTestSupport.Success(await fixture.Client(RequestCqrsRf3Protocol.Node1)
            .ConfigureApiKeyAsync(Guid.NewGuid(), definition, token)).IsSuccess).IsTrue();
        secret = credential;
    }

    internal async Task JoinAfterStopAsync(bool appStopped, List<Exception> failures)
    {
        secret = null;
        if (!appStopped)
        { controls.RetainEvidence(); return; }
        await ServerFailureObserver.ObserveAsync(controls.DisposeAfterResourcesJoinedAsync, failures);
        if (failures.Count != 0)
        { controls.RetainEvidence(); }
    }
}
