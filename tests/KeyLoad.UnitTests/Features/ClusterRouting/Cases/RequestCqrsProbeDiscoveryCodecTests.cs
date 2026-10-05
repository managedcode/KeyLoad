using System.Text;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsProbeDiscoveryCodecTests
{
    private const string Session = "391c37976d1a4e6290123c83b03aeedd";
    private const string Observer = "node1";
    private const string Peer = "node2";

    [Test]
    public async Task DiscoveryRecordRoundTripsTheActualTransportFlagAndOnlyWhitelistedFields()
    {
        var record = CreateRecord(transportReady: true);
        var bytes = RequestCqrsProbeJson.WriteDiscovery(record);
        var actual = RequestCqrsProbeJson.ReadDiscovery(bytes);
        await Assert.That(actual).IsEqualTo(record);
        var json = Encoding.UTF8.GetString(bytes);
        await Assert.That(json).Contains("\"TransportReady\":true");
        await Assert.That(json).Contains("\"ProtocolCompatible\":false");
        await Assert.That(json).DoesNotContain("Address");
        await Assert.That(json).DoesNotContain("Signature");
    }

    [Test]
    public async Task DiscoveryRecordRejectsMalformedIdentityShapeAndCompatibleObservations()
    {
        var bytes = RequestCqrsProbeJson.WriteDiscovery(CreateRecord(transportReady: false));
        var json = Encoding.UTF8.GetString(bytes);
        foreach (var invalid in new[]
        {
            json.Replace("\"ObserverVoterId\":\"node1\",", "", StringComparison.Ordinal),
            json.Replace("\"ProtocolCompatible\":false", "\"ProtocolCompatible\":true", StringComparison.Ordinal),
            json.Replace("\"PeerVoterId\":\"node2\"", "\"PeerVoterId\":\"node1\"", StringComparison.Ordinal),
            json.Replace("\"PeerEnvelopeVersion\":3", "\"PeerEnvelopeVersion\":-1", StringComparison.Ordinal),
            json.Replace("\"SessionId\":\"" + Session + "\"", "\"SessionId\":\"bad\"", StringComparison.Ordinal),
            json.Replace("\"ObserverVoterId\":\"node1\"", "\"ObserverVoterId\":\" \"", StringComparison.Ordinal),
            json[..^1] + ",\"Unexpected\":1}",
            json[..^1] + ",\"ProtocolCompatible\":false}"
        })
        {
            var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                RequestCqrsProbeJson.ReadDiscovery(Encoding.UTF8.GetBytes(invalid)));
            await Assert.That(error.Message).IsEqualTo(RequestCqrsProbeProtocol.InvalidRecord);
        }
    }

    private static RequestCqrsProbeDiscoveryRecord CreateRecord(bool transportReady)
        => new(RequestCqrsProbeProtocol.Version, RequestCqrsProbeProtocol.DiscoveryKind, Session,
            Observer, Peer, 3, 3, transportReady, false);
}
