using KeyLoad.Orleans.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.ResourceExecution.CacheControlWireAuthenticatorSupport;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheControlWireNestedProofAuthenticationTests
{
    [Test]
    public async Task AcCache014ValidOuterMacCannotHideEachInvalidNestedProofMac()
    {
        using var authenticator = CreateAuthenticator();
        var signedGrant = SignGrant(authenticator, CacheControlWireTestData.GrantRequest());
        var signedPrepare = SignPrepare(authenticator, CacheControlWireTestData.PrepareRequest());
        var signedProof = SignProof(authenticator, CacheVoterSlot.Slot0, CacheVoterSlot.Slot1);
        var signedReady = SignPrepareReply(authenticator, signedPrepare, signedProof);
        var validGrantMac = CacheControlWireHmacTestSupport.ComputeOuterMac(signedGrant);
        var validReplyMac = CacheControlWireHmacTestSupport.ComputeOuterMac(signedReady);
        var invalidNestedReply = signedReady with
        {
            Proof = signedReady.Proof! with { Mac = CacheControlWireTestData.Digest(0x89) }
        };

        await Assert.That(validGrantMac.FixedTimeEquals(signedGrant.Mac)).IsTrue();
        await Assert.That(authenticator.TryAuthenticate(signedGrant with { Mac = validGrantMac })).IsTrue();
        await Assert.That(validReplyMac.FixedTimeEquals(signedReady.Mac)).IsTrue();
        await Assert.That(authenticator.TryAuthenticate(signedReady with { Mac = validReplyMac })).IsTrue();
        await AssertForgedGrantProofsRejectedAsync(authenticator, signedGrant);
        await AssertForgedPrepareProofRejectedAsync(authenticator, invalidNestedReply);
    }

    private static async Task AssertForgedGrantProofsRejectedAsync(
        CacheControlAuthenticator authenticator, CacheGrantRequest signedGrant)
    {
        foreach (var slot in Slots)
        {
            var invalid = MutateNestedGrantProof(signedGrant, slot);
            var signAccepted = authenticator.TrySign(invalid, out var signed);
            var completeAccepted = CacheControlWire.TryEncodeSigned(invalid, out var complete);
            var signingAccepted = CacheControlWire.TryEncodeForSigning(invalid, out var signing);
            var forged = invalid with { Mac = CacheControlWireHmacTestSupport.ComputeOuterMac(invalid) };

            await Assert.That(signAccepted).IsFalse();
            await Assert.That(signed).IsNull();
            await Assert.That(completeAccepted).IsTrue();
            await Assert.That(complete.Length > 0).IsTrue();
            await Assert.That(signingAccepted).IsTrue();
            await Assert.That(signing.Length > 0).IsTrue();
            await Assert.That(authenticator.TryAuthenticate(forged)).IsFalse();
        }
    }

    private static async Task AssertForgedPrepareProofRejectedAsync(
        CacheControlAuthenticator authenticator, CachePrepareReply invalid)
    {
        var signAccepted = authenticator.TrySign(invalid, out var signed);
        var completeAccepted = CacheControlWire.TryEncodeSigned(invalid, out var complete);
        var signingAccepted = CacheControlWire.TryEncodeForSigning(invalid, out var signing);
        var forged = invalid with { Mac = CacheControlWireHmacTestSupport.ComputeOuterMac(invalid) };

        await Assert.That(signAccepted).IsFalse();
        await Assert.That(signed).IsNull();
        await Assert.That(completeAccepted).IsTrue();
        await Assert.That(complete.Length > 0).IsTrue();
        await Assert.That(signingAccepted).IsTrue();
        await Assert.That(signing.Length > 0).IsTrue();
        await Assert.That(authenticator.TryAuthenticate(forged)).IsFalse();
    }

    private static CacheGrantRequest MutateNestedGrantProof(CacheGrantRequest grant, int slot)
    {
        var proof = GrantProof(grant, slot) with { Mac = CacheControlWireTestData.Digest((byte)(0x80 + slot)) };
        return slot switch
        {
            0 => grant with { Slot0Proof = proof },
            1 => grant with { Slot1Proof = proof },
            2 => grant with { Slot2Proof = proof },
            _ => throw new ArgumentOutOfRangeException(nameof(slot))
        };
    }

    private static CacheReadyProof GrantProof(CacheGrantRequest grant, int slot)
        => slot switch
        {
            0 => grant.Slot0Proof,
            1 => grant.Slot1Proof,
            2 => grant.Slot2Proof,
            _ => throw new ArgumentOutOfRangeException(nameof(slot))
        };

    private static readonly int[] Slots = [0, 1, 2];
}
