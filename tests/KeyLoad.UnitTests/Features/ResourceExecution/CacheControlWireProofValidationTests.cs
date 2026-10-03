using KeyLoad.Orleans.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheControlWireProofValidationTests
{
    [Test]
    public async Task AcCache014ReadyProofRejectsEveryRequiredDefaultAndInvalidRange()
    {
        var proof = CacheControlWireTestData.Proof(CacheVoterSlot.Slot0, CacheVoterSlot.Slot0);
        var binding = proof.Binding;
        ICacheControlMessage[] invalid =
        [
            proof with { ScopeHash = default },
            proof with { PolicyHash = default },
            proof with { PolicyRevision = 0 },
            proof with { OriginSlot = (CacheVoterSlot)3 },
            proof with { CoordinatorSessionId = Guid.Empty },
            proof with { RoundNonce = Guid.Empty },
            proof with { ChallengeId = Guid.Empty },
            proof with { ChallengeSequence = 0 },
            proof with { ChallengeSequence = -1 },
            proof with { Binding = binding with { Slot = (CacheVoterSlot)3 } },
            proof with { Binding = binding with { NodeId = Guid.Empty } },
            proof with { Binding = binding with { Incarnation = Guid.Empty } },
            proof with { Binding = binding with { RuntimeId = Guid.Empty } },
            proof with { Binding = binding with { Role = (CachePhysicalRole)2 } },
            proof with { Binding = binding with { SiloAddress = NonCanonicalAddress } },
            proof with { Binding = binding with { SiloAddress = OversizedAddress } }
        ];

        foreach (var message in invalid)
        {
            var encoded = CacheControlWire.TryEncodeSigned(message, out var bytes);
            await Assert.That(encoded).IsFalse();
            await Assert.That(bytes.Length).IsEqualTo(0);
        }
    }

    private const string NonCanonicalAddress = "127.0.0.1:11111";
    private static readonly string OversizedAddress = new('x', MaximumAddressCharacters + 1);
    private const int MaximumAddressCharacters = 256;
}
