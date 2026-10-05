using System.Net;
using KeyLoad.Core;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ReplicaMembershipAuthorityNativeContractTests
{
    private const string Nonce = "AAAAAAAAAAAAAAAAAAAAAA";
    private const string Cluster = "membership-native-cluster";
    private const string Voter = "http://node4:8080";
    private const string Etag = "7";
    private const int NativePort = 11111;
    private const int ProxyPort = 30001;
    private static readonly DateTime Started = DateTime.UnixEpoch;

    [Test]
    public async Task GeneratedAuthorityCallAndReplyPreserveNativeFieldsAndExactOriginalBytes()
    {
        var caller = SiloAddress.New(new IPEndPoint(IPAddress.Loopback, NativePort), 9).ToParsableString();
        var call = Call(caller);
        var encodedCall = ReplicaMembershipAuthorityCodec.SerializeCall(call);
        var decodedCall = ReplicaMembershipAuthorityCodec.DeserializeCall(encodedCall);
        await Assert.That(decodedCall.CallerSiloAddress).IsEqualTo(caller);
        await Assert.That(decodedCall.RequestId).IsEqualTo(call.RequestId);
        await Assert.That(decodedCall.Operation).IsEqualTo((int)ReplicaMembershipAuthorityOperation.ReadAll);
        using var mac = new ReplicaMembershipAuthorityMac(new byte[ReplicaMembershipAuthorityProtocol.SecretBytes]);
        var signature = mac.SignRequest(Cluster, call.AuthorityPhysicalShardId.ToString("N"),
            call.AuthorityIncarnation.ToString("N"), call.CallerPhysicalShardId.ToString("N"),
            call.CallerIncarnation.ToString("N"), Voter, caller, "638000000000000000", Nonce, encodedCall);
        await Assert.That(mac.VerifyRequest(Cluster, call.AuthorityPhysicalShardId.ToString("N"),
            call.AuthorityIncarnation.ToString("N"), call.CallerPhysicalShardId.ToString("N"),
            call.CallerIncarnation.ToString("N"), Voter, caller, "638000000000000000", Nonce,
            encodedCall, signature)).IsTrue();
        encodedCall[0] ^= 1;
        await Assert.That(mac.VerifyRequest(Cluster, call.AuthorityPhysicalShardId.ToString("N"),
            call.AuthorityIncarnation.ToString("N"), call.CallerPhysicalShardId.ToString("N"),
            call.CallerIncarnation.ToString("N"), Voter, caller, "638000000000000000", Nonce,
            encodedCall, signature)).IsFalse();

        var row = Entry(caller);
        var reply = new ReplicaMembershipAuthorityReplyV1(ReplicaMembershipAuthorityProtocol.Version,
            call.AuthorityPhysicalShardId, call.AuthorityIncarnation, call.RequestId, Nonce,
            (int)ReplicaMembershipAuthorityResultKind.Completed, null,
            (int)ReplicaMembershipAuthorityErrorDetailCode.None, false, 7, Etag, [row]);
        var encodedReply = ReplicaMembershipAuthorityCodec.SerializeReply(reply);
        var decodedReply = ReplicaMembershipAuthorityCodec.DeserializeReply(encodedReply);
        await Assert.That(decodedReply.TableVersion).IsEqualTo(7);
        await Assert.That(decodedReply.TableVersionETag).IsEqualTo(Etag);
        await Assert.That(decodedReply.Rows.Single().Suspects.Single().Address).IsEqualTo(caller);
        await Assert.That(decodedReply.Rows.Single().RowETag).IsEqualTo(Etag);
    }

    private static ReplicaMembershipAuthorityCallV1 Call(string caller)
        => new(ReplicaMembershipAuthorityProtocol.Version, Cluster, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Voter, caller, Guid.NewGuid(),
            (int)ReplicaMembershipAuthorityOperation.ReadAll, null, null, 0, null, null, 0);

    private static ReplicaMembershipAuthorityEntryV1 Entry(string address)
        => new(address, SiloStatus.Active, ProxyPort, "membership-native-host", "membership-native-silo",
            Started, Started.AddMinutes(1), [new(address, Started.AddSeconds(3))], Etag);
}
