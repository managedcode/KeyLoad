using KeyLoad.Orleans.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheControlWireGrantCohortTests
{
    [Test]
    public async Task AcCache014GrantCohortRejectsEverySharedProofFieldMismatchAndDuplicateAddress()
    {
        var request = CacheControlWireTestData.GrantRequest();
        var slot1 = request.Slot1Proof;
        var slot2 = request.Slot2Proof;
        ICacheControlMessage[] invalid =
        [
            request with { Slot1Proof = slot1 with { ScopeHash = CacheControlWireTestData.Digest(0x81) } },
            request with { Slot1Proof = slot1 with { PolicyHash = CacheControlWireTestData.Digest(0x82) } },
            request with { Slot1Proof = slot1 with { PolicyRevision = slot1.PolicyRevision + 1 } },
            request with { Slot1Proof = slot1 with { OriginSlot = CacheVoterSlot.Slot1 } },
            request with { Slot1Proof = slot1 with { CoordinatorSessionId = DifferentSessionId } },
            request with { Slot1Proof = slot1 with { RoundNonce = DifferentRoundNonce } },
            request with
            {
                Slot1Proof = slot1 with
                {
                    Binding = slot1.Binding with { SiloAddress = request.Slot0Proof.Binding.SiloAddress }
                }
            },
            request with
            {
                Slot2Proof = slot2 with
                {
                    Binding = slot2.Binding with { SiloAddress = request.Slot1Proof.Binding.SiloAddress }
                }
            }
        ];

        foreach (var changed in invalid)
        {
            var encoded = CacheControlWire.TryEncodeSigned(changed, out var bytes);
            await Assert.That(encoded).IsFalse();
            await Assert.That(bytes.Length).IsEqualTo(0);
        }
    }

    private static readonly Guid DifferentSessionId = Guid.Parse("a1234567-89ab-cdef-8012-3456789abcde");
    private static readonly Guid DifferentRoundNonce = Guid.Parse("b1234567-89ab-cdef-8012-3456789abcde");
}
