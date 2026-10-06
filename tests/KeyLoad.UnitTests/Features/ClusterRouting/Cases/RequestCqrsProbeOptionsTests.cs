using KeyLoad.Replication;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsProbeOptionsTests
{
    private const string EnabledKey = "KeyLoad:RequestCqrsProbe:Enabled";
    private const string RootKey = "KeyLoad:RequestCqrsProbe:Root";
    private const string SessionKey = "KeyLoad:RequestCqrsProbe:SessionId";
    private const string UnknownKey = "KeyLoad:RequestCqrsProbe:Unknown";
    private const string Session = "391c37976d1a4e6290123c83b03aeedd";
    private const string Voter1 = "http://node1:8080";
    private const string Voter2 = "http://node2:8080";
    private const string Voter3 = "http://node3:8080";

    [Test]
    public async Task CurrentPrivateRf3PhaseProbeSettingsAreAccepted()
    {
        var replica = CreateReplica();
        var absent = RequestCqrsProbeOptionsReader.Read(new ConfigurationBuilder().Build(), replica, true);
        await Assert.That(absent.Enabled).IsFalse();
        var accepted = RequestCqrsProbeOptionsReader.Read(Configuration(new Dictionary<string, string?>
        {
            [EnabledKey] = "true",
            [RootKey] = RequestCqrsProbeProtocol.FixedRoot,
            [SessionKey] = Session
        }), replica, true);
        await Assert.That(accepted.Enabled).IsTrue();
        await Assert.That(accepted.Root).IsEqualTo(RequestCqrsProbeProtocol.FixedRoot);
        await Assert.That(accepted.SessionId).IsEqualTo(Session);
    }

    [Test]
    public async Task CurrentProbeOptionsRejectUnknownAndNonCanonicalConfiguration()
    {
        var replica = CreateReplica();
        foreach (var values in new[]
        {
            new Dictionary<string, string?>
            {
                [EnabledKey] = "true", [RootKey] = RequestCqrsProbeProtocol.FixedRoot,
                [SessionKey] = Session, [UnknownKey] = "true"
            },
            new Dictionary<string, string?>
            {
                [EnabledKey] = "true", [RootKey] = RequestCqrsProbeProtocol.FixedRoot,
                [SessionKey] = "not-a-canonical-guid"
            }
        })
        { await AssertRejectedAsync(Configuration(values), replica); }
        await AssertRejectedAsync(Configuration(new Dictionary<string, string?>
        { [EnabledKey] = "true", [RootKey] = RequestCqrsProbeProtocol.FixedRoot, [SessionKey] = Session }),
            replica, allowPrivateHttp: false);
    }

    private static ReplicaConfiguration CreateReplica()
        => new(Voter1, [Voter1, Voter2, Voter3], "/tmp/request-probe-discovery-options", Guid.NewGuid());

    private static IConfiguration Configuration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static async Task AssertRejectedAsync(IConfiguration configuration, ReplicaConfiguration replica, bool allowPrivateHttp = true)
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            RequestCqrsProbeOptionsReader.Read(configuration, replica, allowPrivateHttp));
        await Assert.That(error.Message).IsEqualTo(RequestCqrsProbeProtocol.InvalidOptions);
    }
}
