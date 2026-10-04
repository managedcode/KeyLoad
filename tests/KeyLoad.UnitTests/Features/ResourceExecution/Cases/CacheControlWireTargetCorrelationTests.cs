using KeyLoad.Orleans.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.ResourceExecution.CacheControlWireAuthenticatorSupport;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheControlWireTargetCorrelationTests
{
    [Test]
    [Arguments(CacheVoterSlot.Slot0, Slot0Sequence, Slot1Sequence)]
    [Arguments(CacheVoterSlot.Slot1, Slot1Sequence, Slot0Sequence)]
    [Arguments(CacheVoterSlot.Slot2, Slot2Sequence, Slot1Sequence)]
    public async Task AcCache014EachTargetUsesItsOwnSignedSequenceAndExactBinding(
        CacheVoterSlot target, long expectedSequence, long foreignSlotSequence)
    {
        using var authenticator = CreateAuthenticator();
        var unsigned = GrantForTarget(target);
        var request = SignGrant(authenticator, unsigned);
        var active = SignGrantReply(authenticator, request);
        var cold = SignGrantReply(authenticator, request, CacheControlStatus.AcceptedCold, expectedSequence);
        var wrongSequence = SignGrantReply(authenticator, request, CacheControlStatus.AcceptedActive, foreignSlotSequence);
        var requestEncoded = CacheControlWire.TryEncodeSigned(request, out var requestBytes);
        var activeEncoded = CacheControlWire.TryEncodeSigned(active, out var activeBytes);
        var coldEncoded = CacheControlWire.TryEncodeSigned(cold, out var coldBytes);
        var wrongEncoded = CacheControlWire.TryEncodeSigned(wrongSequence, out var wrongBytes);

        await Assert.That(requestEncoded).IsTrue();
        await Assert.That(requestBytes.Length > 0).IsTrue();
        await Assert.That(request.Header.TargetSlot).IsEqualTo(target);
        await Assert.That(request.TargetBinding.Slot).IsEqualTo(target);
        await AssertProofMetadataPreservedAsync(unsigned, request);
        await Assert.That(SignedTargetSequence(request)).IsEqualTo(expectedSequence);
        await Assert.That(activeEncoded).IsTrue();
        await Assert.That(activeBytes.Length > 0).IsTrue();
        await Assert.That(coldEncoded).IsTrue();
        await Assert.That(coldBytes.Length > 0).IsTrue();
        await Assert.That(wrongEncoded).IsTrue();
        await Assert.That(wrongBytes.Length > 0).IsTrue();
        await Assert.That(wrongSequence.AcceptedSequence).IsEqualTo(foreignSlotSequence);
        await Assert.That(IsForeignProofSequence(request, target, foreignSlotSequence)).IsTrue();
        await Assert.That(active.Status).IsEqualTo(CacheControlStatus.AcceptedActive);
        await Assert.That(cold.Status).IsEqualTo(CacheControlStatus.AcceptedCold);
        await Assert.That(active.AcceptedBinding).IsEqualTo(request.TargetBinding);
        await Assert.That(cold.AcceptedBinding).IsEqualTo(request.TargetBinding);
        await Assert.That(CacheControlCorrelation.TryMatch(request, active)).IsTrue();
        await Assert.That(CacheControlCorrelation.TryMatch(request, cold)).IsTrue();
        await Assert.That(CacheControlCorrelation.TryMatch(request, wrongSequence)).IsFalse();
    }

    private static CacheGrantRequest GrantForTarget(CacheVoterSlot target)
    {
        var request = CacheControlWireTestData.GrantRequest();
        var proof = target switch
        {
            CacheVoterSlot.Slot0 => request.Slot0Proof,
            CacheVoterSlot.Slot1 => request.Slot1Proof,
            CacheVoterSlot.Slot2 => request.Slot2Proof,
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        return request with
        {
            Header = request.Header with { TargetSlot = target },
            TargetBinding = proof.Binding
        };
    }

    private static async Task AssertProofMetadataPreservedAsync(CacheGrantRequest before, CacheGrantRequest after)
    {
        await Assert.That(after.Slot0Proof with { Mac = before.Slot0Proof.Mac }).IsEqualTo(before.Slot0Proof);
        await Assert.That(after.Slot1Proof with { Mac = before.Slot1Proof.Mac }).IsEqualTo(before.Slot1Proof);
        await Assert.That(after.Slot2Proof with { Mac = before.Slot2Proof.Mac }).IsEqualTo(before.Slot2Proof);
    }

    private static long SignedTargetSequence(CacheGrantRequest request)
        => request.Header.TargetSlot switch
        {
            CacheVoterSlot.Slot0 => request.Slot0Proof.ChallengeSequence,
            CacheVoterSlot.Slot1 => request.Slot1Proof.ChallengeSequence,
            CacheVoterSlot.Slot2 => request.Slot2Proof.ChallengeSequence,
            _ => 0
        };

    private static bool IsForeignProofSequence(CacheGrantRequest request, CacheVoterSlot target, long sequence)
        => (target != CacheVoterSlot.Slot0 && request.Slot0Proof.ChallengeSequence == sequence)
            || (target != CacheVoterSlot.Slot1 && request.Slot1Proof.ChallengeSequence == sequence)
            || (target != CacheVoterSlot.Slot2 && request.Slot2Proof.ChallengeSequence == sequence);

    private const long Slot0Sequence = 12;
    private const long Slot1Sequence = 13;
    private const long Slot2Sequence = 14;
}
