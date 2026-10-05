using KeyLoad.Replication;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsProbeDiscoveryOptionsTests
{
    private const string EnabledKey = "KeyLoad:RequestCqrsProbe:Enabled";
    private const string RootKey = "KeyLoad:RequestCqrsProbe:Root";
    private const string SessionKey = "KeyLoad:RequestCqrsProbe:SessionId";
    private const string CaptureKey = "KeyLoad:RequestCqrsProbe:DiscoveryCaptureMode";
    private const string UnknownKey = "KeyLoad:RequestCqrsProbe:Unknown";
    private const string Session = "391c37976d1a4e6290123c83b03aeedd";
    private const string Voter1 = "http://node1:8080";
    private const string Voter2 = "http://node2:8080";
    private const string Voter3 = "http://node3:8080";

    [Test]
    public async Task CaptureModeDefaultsOffAndStrictMixedModeRequiresEnabledPrivateProbe()
    {
        var replica = CreateReplica();
        var absent = RequestCqrsProbeOptionsReader.Read(new ConfigurationBuilder().Build(), replica, true);
        await Assert.That(absent.DiscoveryCaptureMode).IsEqualTo(RequestCqrsProbeProtocol.DiscoveryCaptureDisabled);
        var accepted = RequestCqrsProbeOptionsReader.Read(Configuration(new Dictionary<string, string?>
        {
            [EnabledKey] = "true", [RootKey] = RequestCqrsProbeProtocol.FixedRoot,
            [SessionKey] = Session, [CaptureKey] = RequestCqrsProbeProtocol.MixedInterface3Capture
        }), replica, true);
        await Assert.That(accepted.DiscoveryCaptureMode).IsEqualTo(RequestCqrsProbeProtocol.MixedInterface3Capture);
        await AssertRejectedAsync(Configuration(new Dictionary<string, string?>
        { [CaptureKey] = RequestCqrsProbeProtocol.MixedInterface3Capture }), replica);
        var wrongObserver = replica with { LocalId = Voter2 };
        await AssertRejectedAsync(Configuration(new Dictionary<string, string?>
        {
            [EnabledKey] = "true", [RootKey] = RequestCqrsProbeProtocol.FixedRoot,
            [SessionKey] = Session, [CaptureKey] = RequestCqrsProbeProtocol.MixedInterface3Capture
        }), wrongObserver);
    }

    [Test]
    public async Task CaptureModeRejectsUnknownAndNonCanonicalConfiguration()
    {
        var replica = CreateReplica();
        foreach (var values in new[]
        {
            new Dictionary<string, string?>
            {
                [EnabledKey] = "true", [RootKey] = RequestCqrsProbeProtocol.FixedRoot,
                [SessionKey] = Session, [CaptureKey] = "mixed-interface3"
            },
            new Dictionary<string, string?>
            {
                [EnabledKey] = "false", [CaptureKey] = RequestCqrsProbeProtocol.MixedInterface3Capture
            },
            new Dictionary<string, string?>
            {
                [EnabledKey] = "true", [RootKey] = RequestCqrsProbeProtocol.FixedRoot,
                [SessionKey] = Session, [CaptureKey] = RequestCqrsProbeProtocol.MixedInterface3Capture,
                [UnknownKey] = "true"
            }
        })
        { await AssertRejectedAsync(Configuration(values), replica); }
    }

    private static ReplicaConfiguration CreateReplica()
        => new(Voter1, [Voter1, Voter2, Voter3], "/tmp/request-probe-discovery-options", Guid.NewGuid());

    private static IConfiguration Configuration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static async Task AssertRejectedAsync(IConfiguration configuration, ReplicaConfiguration replica)
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            RequestCqrsProbeOptionsReader.Read(configuration, replica, true));
        await Assert.That(error.Message).IsEqualTo(RequestCqrsProbeProtocol.InvalidOptions);
    }
}
