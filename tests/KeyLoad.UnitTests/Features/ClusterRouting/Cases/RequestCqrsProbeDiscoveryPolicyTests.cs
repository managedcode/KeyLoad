using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsProbeDiscoveryPolicyTests
{
    private const string Session = "391c37976d1a4e6290123c83b03aeedd";
    private const string Local = "http://node1:8080";
    private const string Remote1 = "http://node2:8080";
    private const string Remote2 = "http://node3:8080";
    private static readonly IReadOnlyList<string> Voters = [Local, Remote1, Remote2];

    [Test]
    public async Task DiscoveryUsesFixedOrdinalPeerSlotsAndAllowsReadinessOnlyRepeat()
    {
        var records = CreateRecords();
        var first = CreateRecord(Remote1, transportReady: false);
        records.ValidateDiscoveryInventory([first]);
        records.ValidateDiscoveryForWrite(first with { TransportReady = true });
        await Assert.That(records.GetDiscoverySlot(Remote1)).IsEqualTo(0);
        await Assert.That(records.GetDiscoverySlot(Remote2)).IsEqualTo(1);
    }

    [Test]
    public async Task DiscoveryRejectsChangedProtocolIdentityAndBeyondTwoPeerSlots()
    {
        var records = CreateRecords();
        var first = CreateRecord(Remote1, transportReady: false);
        records.ValidateDiscoveryInventory([first]);
        var changed = first with { ApplicationRpcVersion = 4 };
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => records.ValidateDiscoveryForWrite(changed));
        await Assert.That(error.Message).IsEqualTo(RequestCqrsProbeProtocol.InvalidFiles);
        var unknown = Assert.ThrowsExactly<InvalidOperationException>(() => records.GetDiscoverySlot("http://other:8080"));
        await Assert.That(unknown.Message).IsEqualTo(RequestCqrsProbeProtocol.InvalidFiles);
    }

    private static RequestCqrsProbeRecords CreateRecords()
        => new(Session, Local, [], new RequestCqrsProbeDiscoveryPolicy(Voters, Local, enabled: true));

    private static RequestCqrsProbeDiscoveryRecord CreateRecord(string peer, bool transportReady)
        => new(RequestCqrsProbeProtocol.Version, RequestCqrsProbeProtocol.DiscoveryKind, Session,
            Local, peer, 3, 3, transportReady, false);
}
