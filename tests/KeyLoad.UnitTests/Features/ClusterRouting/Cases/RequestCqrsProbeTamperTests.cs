using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsProbeTamperTests
{
    [Test]
    public async Task EveryOriginalIdentityAndConnectionFieldIsAuthenticated()
    {
        using var fixture = new ReplicaSecurityFixture();
        using var mac = new PartitionMovementMac(fixture.Options.Secret.Span, PhysicalOwnerProbeProtocol.MaximumBodyBytes);
        var request = RequestCqrsProbeNativeWireValues.Request(fixture);
        request = request with { Signature = mac.SignProbeRequest(request) };
        var reply = RequestCqrsProbeNativeWireValues.Reply(request);
        reply = reply with { Signature = mac.SignProbeReply(reply) };
        var identity = request.Identity;
        var changed = new[]
        {
            identity with { RequestId = Guid.NewGuid() },
            identity with { CommandId = Guid.NewGuid() },
            identity with { PrincipalId = "changed-principal" },
            identity with { ReadKind = GrainReadKind.Document },
            identity with { CommandKind = null },
            identity with { OriginalExpiresAt = identity.OriginalExpiresAt.AddTicks(1) },
            identity with { ConnectionId = Guid.NewGuid() }
        };
        foreach (var mutation in changed)
        {
            await Assert.That(mac.VerifyProbeRequest(request with { Identity = mutation })).IsFalse();
            await Assert.That(mac.VerifyProbeRequest(request with { Claim = request.Claim! with { Identity = mutation } })).IsFalse();
            await Assert.That(mac.VerifyProbeReply(reply with { Claim = reply.Claim! with { Identity = mutation } })).IsFalse();
        }
    }

    [Test]
    public async Task PurposeOwnersVotersFullGenerationsNonceAndDeadlineAreAuthenticated()
    {
        using var fixture = new ReplicaSecurityFixture();
        using var mac = new PartitionMovementMac(fixture.Options.Secret.Span, PhysicalOwnerProbeProtocol.MaximumBodyBytes);
        var request = RequestCqrsProbeNativeWireValues.Request(fixture);
        request = request with { Signature = mac.SignProbeRequest(request) };
        var reply = RequestCqrsProbeNativeWireValues.Reply(request);
        reply = reply with { Signature = mac.SignProbeReply(reply) };
        var owner = request.CallerOwner;
        var changedOwners = new[]
        {
            owner with { PhysicalShardId = Guid.NewGuid() },
            owner with { Incarnation = Guid.NewGuid() },
            owner with { VoterIds = [ReplicaSecurityFixture.VoterA, ReplicaSecurityFixture.VoterB, ReplicaSecurityFixture.UnknownVoter] },
            owner with { PlacementEpoch = owner.PlacementEpoch + 1 }
        };
        foreach (var changed in changedOwners)
        {
            await Assert.That(mac.VerifyProbeRequest(request with { CallerOwner = changed })).IsFalse();
            await Assert.That(mac.VerifyProbeRequest(request with { DestinationOwner = changed })).IsFalse();
            await Assert.That(mac.VerifyProbeRequest(request with { Claim = request.Claim! with { Owner = changed } })).IsFalse();
            await Assert.That(mac.VerifyProbeReply(reply with { Claim = reply.Claim! with { Owner = changed } })).IsFalse();
        }
        var mutations = new[]
        {
            request with { Version = request.Version + 1 },
            request with { Method = RequestCqrsProbeNativeMethod.Locate },
            request with { CallerVoter = ReplicaSecurityFixture.UnknownVoter },
            request with { CallerSiloAddress = fixture.OtherGeneration },
            request with { DestinationVoter = ReplicaSecurityFixture.UnknownVoter },
            request with { DestinationSiloAddress = fixture.OtherGeneration },
            request with { Nonce = Guid.NewGuid() },
            request with { ExpiresAt = request.ExpiresAt.AddTicks(1) },
            request with { Claim = null }
        };
        foreach (var mutation in mutations)
        { await Assert.That(mac.VerifyProbeRequest(mutation)).IsFalse(); }
    }

    [Test]
    public async Task SessionArmDigestAndCompleteClaimAreBoundToBothRequestAndReply()
    {
        using var fixture = new ReplicaSecurityFixture();
        using var mac = new PartitionMovementMac(fixture.Options.Secret.Span, PhysicalOwnerProbeProtocol.MaximumBodyBytes);
        var request = RequestCqrsProbeNativeWireValues.Request(fixture);
        request = request with { Signature = mac.SignProbeRequest(request) };
        var reply = RequestCqrsProbeNativeWireValues.Reply(request);
        reply = reply with { Signature = mac.SignProbeReply(reply) };
        var claim = request.Claim!;
        var changed = new[]
        {
            claim with { SessionId = "changed-session" },
            claim with { ArmId = Guid.NewGuid() },
            claim with { Identity = claim.Identity with { ConnectionId = Guid.NewGuid() } },
            claim with { Owner = claim.Owner with { Incarnation = Guid.NewGuid() } },
            claim with { Voter = ReplicaSecurityFixture.UnknownVoter },
            claim with { SiloAddress = fixture.OtherGeneration },
            claim with { ArmSha256 = (claim.ArmSha256[0] == '0' ? "1" : "0") + claim.ArmSha256[1..] }
        };
        foreach (var mutation in changed)
        {
            await Assert.That(mac.VerifyProbeRequest(request with { Claim = mutation })).IsFalse();
            await Assert.That(mac.VerifyProbeReply(reply with { Claim = mutation })).IsFalse();
        }
    }
}
