using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsProbeWireTests
{
    [Test]
    public async Task FrozenAliasesAndFieldIdsRemainExactForEveryNativeDiagnosticContract()
    {
        await RequestCqrsProbeNativeWireValues.AssertContract<RequestCqrsProbeNativeIdentity>("keyload.request-cqrs-probe-native-identity.v1",
            "RequestId", "CommandId", "PrincipalId", "ReadKind", "CommandKind", "OriginalExpiresAt", "ConnectionId");
        await RequestCqrsProbeNativeWireValues.AssertContract<RequestCqrsProbeNativeClaim>("keyload.request-cqrs-probe-native-claim.v1",
            "SessionId", "ArmId", "Identity", "Owner", "Voter", "SiloAddress", "ArmSha256");
        await RequestCqrsProbeNativeWireValues.AssertContract<RequestCqrsProbeNativeRequest>("keyload.request-cqrs-probe-native-request.v1",
            "Version", "Method", "Identity", "CallerOwner", "CallerVoter", "CallerSiloAddress", "DestinationOwner",
            "DestinationVoter", "DestinationSiloAddress", "Nonce", "ExpiresAt", "Claim", "Signature");
        await RequestCqrsProbeNativeWireValues.AssertContract<RequestCqrsProbeNativeReply>("keyload.request-cqrs-probe-native-reply.v1",
            "Version", "Method", "Nonce", "Claim", "Acknowledged", "Error", "Signature");
        await RequestCqrsProbeNativeWireValues.AssertContract<RequestCqrsProbeNativeMethod>("keyload.request-cqrs-probe-native-method.v1");
        var methods = Enum.GetValues<RequestCqrsProbeNativeMethod>();
        await Assert.That(methods.Select(value => (int)value)).IsEquivalentTo(new[] { 0, 1 },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(methods.Select(value => value.ToString())).IsEquivalentTo(
            new[] { nameof(RequestCqrsProbeNativeMethod.Locate), nameof(RequestCqrsProbeNativeMethod.Notify) },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task CompleteNotifyIdentityClaimRequestAndReplyRoundTripThroughGeneratedCodec()
    {
        using var fixture = new ReplicaSecurityFixture();
        using var mac = new PartitionMovementMac(fixture.Options.Secret.Span, PhysicalOwnerProbeProtocol.MaximumBodyBytes);
        var request = RequestCqrsProbeNativeWireValues.Request(fixture);
        request = request with
        {
            Signature = mac.SignProbeRequest(request)
        };
        var reply = RequestCqrsProbeNativeWireValues.Reply(request);
        reply = reply with
        {
            Signature = mac.SignProbeReply(reply)
        };
        await RequestCqrsProbeNativeWireValues.AssertExactRoundTrip(request.Identity);
        await RequestCqrsProbeNativeWireValues.AssertExactRoundTrip(request.Claim!);
        await RequestCqrsProbeNativeWireValues.AssertExactRoundTrip(request);
        await RequestCqrsProbeNativeWireValues.AssertExactRoundTrip(reply);
        await Assert.That(mac.VerifyProbeRequest(NativeSerialization.Deserialize<RequestCqrsProbeNativeRequest>(NativeSerialization.Serialize(request)))).IsTrue();
        await Assert.That(mac.VerifyProbeReply(NativeSerialization.Deserialize<RequestCqrsProbeNativeReply>(NativeSerialization.Serialize(reply)))).IsTrue();
    }

    [Test]
    public async Task LocateWithNoClaimAndNoAcknowledgementPreservesNullableFields()
    {
        using var fixture = new ReplicaSecurityFixture();
        var request = RequestCqrsProbeNativeWireValues.Request(fixture) with
        {
            Method = RequestCqrsProbeNativeMethod.Locate,
            Claim = null,
            Identity = new(Guid.NewGuid(), Guid.Empty, null, null, null,
            RequestCqrsProbeNativeWireValues.Now.AddMinutes(1), Guid.NewGuid())
        };
        var reply = RequestCqrsProbeNativeWireValues.Reply(request);
        await RequestCqrsProbeNativeWireValues.AssertExactRoundTrip(request);
        await RequestCqrsProbeNativeWireValues.AssertExactRoundTrip(reply);
        var decoded = NativeSerialization.Deserialize<RequestCqrsProbeNativeReply>(NativeSerialization.Serialize(reply));
        await Assert.That(decoded.Claim is null).IsTrue();
        await Assert.That(decoded.Acknowledged).IsFalse();
        await Assert.That(decoded.Error is null).IsTrue();
    }
}
