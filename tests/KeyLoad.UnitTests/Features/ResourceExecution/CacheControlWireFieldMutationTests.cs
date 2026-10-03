using System.Net;
using KeyLoad.Orleans.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheControlWireFieldMutationTests
{
    [Test]
    public async Task AcCache014PrepareTranscriptBindsEveryHeaderAndAddressField()
    {
        var request = CacheControlWireTestData.PrepareRequest();
        var original = Canonical(request);
        ICacheControlMessage[] changed =
        [
            request with { Header = request.Header with { ScopeHash = CacheControlWireTestData.Digest(0x81) } },
            request with { Header = request.Header with { PolicyHash = CacheControlWireTestData.Digest(0x82) } },
            request with { Header = request.Header with { PolicyRevision = request.Header.PolicyRevision + 1 } },
            request with { Header = request.Header with { OriginSlot = CacheVoterSlot.Slot1 } },
            request with { Header = request.Header with { TargetSlot = CacheVoterSlot.Slot2 }, ExpectedTargetSiloAddress = CacheControlWireTestData.Address(CacheVoterSlot.Slot2) },
            request with { Header = request.Header with { CoordinatorSessionId = ChangedSessionId } },
            request with { Header = request.Header with { RoundNonce = ChangedRoundNonce } },
            request with { Header = request.Header with { RequestNonce = ChangedRequestNonce } },
            request with { Header = request.Header with { SentUnixMilliseconds = request.Header.SentUnixMilliseconds + 1 } },
            request with { ExpectedTargetSiloAddress = CacheControlWireTestData.Address(CacheVoterSlot.Slot0) }
        ];

        foreach (var message in changed)
        {
            await AssertCanonicalChangedAsync(original, message);
        }
    }

    [Test]
    public async Task AcCache014ReadyProofAndBindingTranscriptBindsEveryNonMacField()
    {
        var proof = CacheControlWireTestData.Proof(CacheVoterSlot.Slot0, CacheVoterSlot.Slot0);
        var canonical = Canonical(proof);
        var signing = Signing(proof);
        ICacheControlMessage[] changed =
        [
            proof with { ScopeHash = CacheControlWireTestData.Digest(0x91) },
            proof with { PolicyHash = CacheControlWireTestData.Digest(0x92) },
            proof with { PolicyRevision = proof.PolicyRevision + 1 },
            proof with { OriginSlot = CacheVoterSlot.Slot1 },
            proof with { CoordinatorSessionId = ChangedSessionId },
            proof with { RoundNonce = ChangedRoundNonce },
            proof with { ChallengeId = ChangedChallengeId },
            proof with { ChallengeSequence = proof.ChallengeSequence + 1 },
            proof with { Binding = proof.Binding with { Slot = CacheVoterSlot.Slot1 } },
            proof with { Binding = proof.Binding with { NodeId = ChangedNodeId } },
            proof with { Binding = proof.Binding with { Incarnation = ChangedIncarnationId } },
            proof with { Binding = proof.Binding with { SiloAddress = SiloAddress.New(IPAddress.Parse("127.0.0.9"), 11119, 7).ToParsableString() } },
            proof with { Binding = proof.Binding with { RuntimeId = ChangedRuntimeId } }
        ];

        foreach (var mutation in changed)
        {
            await AssertCanonicalChangedAsync(canonical, mutation);
            await AssertSigningChangedAsync(signing, mutation);
        }

        var macMutation = proof with { Mac = CacheControlWireTestData.Digest(0xA2) };
        await AssertCanonicalChangedAsync(canonical, macMutation);
        await AssertSigningUnchangedAsync(signing, macMutation);
    }

    [Test]
    public async Task AcCache014RequiredReferencesAndCanonicalTextRejectWithoutPartialOutput()
    {
        var prepare = CacheControlWireTestData.PrepareRequest();
        var grant = CacheControlWireTestData.GrantRequest();
        var revoke = CacheControlWireTestData.RevokeRequest();
        var refresh = CacheControlWireTestData.RefreshHint();
        ICacheControlMessage[] missing =
        [
            prepare with { Header = null! },
            grant with { Header = null! },
            grant with { TargetBinding = null! },
            grant with { Slot0Proof = null! },
            grant with { Slot1Proof = null! },
            grant with { Slot2Proof = null! },
            revoke with { Header = null! },
            revoke with { TargetBinding = null! },
            refresh with { Header = null! },
            refresh with { SenderBinding = null! },
            CacheControlWireTestData.Proof(CacheVoterSlot.Slot0, CacheVoterSlot.Slot0) with { Binding = null! },
            CacheControlWireTestData.Proof(CacheVoterSlot.Slot0, CacheVoterSlot.Slot0) with { Version = 2 },
            CacheControlWireTestData.Proof(CacheVoterSlot.Slot0, CacheVoterSlot.Slot0) with { Status = CacheControlStatus.Busy },
            CacheControlWireTestData.Proof(CacheVoterSlot.Slot0, CacheVoterSlot.Slot0) with
            {
                Binding = CacheControlWireTestData.Binding(CacheVoterSlot.Slot0) with { NodeId = Guid.Empty }
            },
            CacheControlWireTestData.Proof(CacheVoterSlot.Slot0, CacheVoterSlot.Slot0) with
            {
                Binding = CacheControlWireTestData.Binding(CacheVoterSlot.Slot0) with { SiloAddress = null! }
            },
            prepare with { ExpectedTargetSiloAddress = new string('x', MaximumAddressCharacters + 1) },
            prepare with { ExpectedTargetSiloAddress = InvalidUtf16Address }
        ];

        foreach (var message in missing)
        {
            var encoded = CacheControlWire.TryEncodeSigned(message, out var bytes);
            await Assert.That(encoded).IsFalse();
            await Assert.That(bytes.Length).IsEqualTo(0);
        }
    }

    [Test]
    public async Task AcCache014UnixMillisecondBoundariesAreInclusiveAndOverflowEdgesReject()
    {
        var request = CacheControlWireTestData.PrepareRequest();
        var minimum = request with { Header = request.Header with { SentUnixMilliseconds = MinimumUnixMilliseconds } };
        var maximum = request with { Header = request.Header with { SentUnixMilliseconds = MaximumUnixMilliseconds } };
        var below = request with { Header = request.Header with { SentUnixMilliseconds = MinimumUnixMilliseconds - 1 } };
        var above = request with { Header = request.Header with { SentUnixMilliseconds = MaximumUnixMilliseconds + 1 } };

        await Assert.That(Encode(minimum)).IsTrue();
        await Assert.That(Encode(maximum)).IsTrue();
        await Assert.That(Encode(below)).IsFalse();
        await Assert.That(Encode(above)).IsFalse();
    }

    [Test]
    public async Task AcCache014NativeAddressParserRoundTripsCanonicalNumericEdgesOnly()
    {
        var request = CacheControlWireTestData.PrepareRequest();
        var zero = request with
        {
            ExpectedTargetSiloAddress = SiloAddress.New(IPAddress.Loopback, 0, 0).ToParsableString()
        };
        var upper = request with
        {
            ExpectedTargetSiloAddress = SiloAddress.New(IPAddress.Loopback, ushort.MaxValue, int.MaxValue).ToParsableString()
        };
        var noncanonicalAndOverflow = new[]
        {
            request with { ExpectedTargetSiloAddress = LeadingZeroPortAddress },
            request with { ExpectedTargetSiloAddress = OverflowPortAddress },
            request with { ExpectedTargetSiloAddress = NegativePortAddress },
            request with { ExpectedTargetSiloAddress = OverflowGenerationAddress }
        };

        await Assert.That(Encode(zero)).IsTrue();
        await Assert.That(Encode(upper)).IsTrue();
        foreach (var invalid in noncanonicalAndOverflow)
        {
            await Assert.That(Encode(invalid)).IsFalse();
        }
    }

    private static async Task AssertCanonicalChangedAsync(byte[] original, ICacheControlMessage changed)
    {
        var mutated = Canonical(changed);
        await Assert.That(mutated.AsSpan().SequenceEqual(original)).IsFalse();
    }

    private static async Task AssertSigningChangedAsync(byte[] original, ICacheControlMessage changed)
    {
        var mutated = Signing(changed);
        await Assert.That(mutated.AsSpan().SequenceEqual(original)).IsFalse();
    }

    private static async Task AssertSigningUnchangedAsync(byte[] original, ICacheControlMessage changed)
    {
        var mutated = Signing(changed);
        await Assert.That(mutated.AsSpan().SequenceEqual(original)).IsTrue();
    }

    private static byte[] Canonical(ICacheControlMessage message)
    {
        var encoded = CacheControlWire.TryEncodeSigned(message, out var bytes);
        return encoded ? bytes : throw new InvalidOperationException(InvalidTranscriptMessage);
    }

    private static byte[] Signing(ICacheControlMessage message)
    {
        var encoded = CacheControlWire.TryEncodeForSigning(message, out var bytes);
        return encoded ? bytes : throw new InvalidOperationException(InvalidTranscriptMessage);
    }

    private static bool Encode(ICacheControlMessage message)
    {
        var encoded = CacheControlWire.TryEncodeSigned(message, out var bytes);
        if (!encoded && bytes.Length != 0)
        {
            throw new InvalidOperationException(PartialOutputMessage);
        }

        return encoded;
    }

    private const int MaximumAddressCharacters = 256;
    private const long MinimumUnixMilliseconds = -62_135_596_800_000;
    private const long MaximumUnixMilliseconds = 253_402_300_799_999;
    private const string InvalidUtf16Address = "\uD800";
    private const string LeadingZeroPortAddress = "127.0.0.1:011111@7";
    private const string OverflowPortAddress = "127.0.0.1:65536@0";
    private const string NegativePortAddress = "127.0.0.1:-1@0";
    private const string OverflowGenerationAddress = "127.0.0.1:11111@2147483648";
    private static readonly Guid ChangedSessionId = Guid.Parse("00000000-0000-0000-0000-000000000401");
    private static readonly Guid ChangedRoundNonce = Guid.Parse("00000000-0000-0000-0000-000000000402");
    private static readonly Guid ChangedRequestNonce = Guid.Parse("00000000-0000-0000-0000-000000000403");
    private static readonly Guid ChangedChallengeId = Guid.Parse("00000000-0000-0000-0000-000000000404");
    private static readonly Guid ChangedNodeId = Guid.Parse("00000000-0000-0000-0000-000000000405");
    private static readonly Guid ChangedIncarnationId = Guid.Parse("00000000-0000-0000-0000-000000000406");
    private static readonly Guid ChangedRuntimeId = Guid.Parse("00000000-0000-0000-0000-000000000407");
    private const string InvalidTranscriptMessage = "The valid mutation source did not encode.";
    private const string PartialOutputMessage = "Invalid input must leave the output byte array empty.";
}
