using System.Net;
using KeyLoad.Orleans.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheControlWireShapeTests
{
    private const byte WireVersion = 1;
    private const long PolicyRevision = 4;
    private const long ValidTimestamp = 1_798_992_000_000;
    private const long MinimumUnixMilliseconds = -62_135_596_800_000;
    private const long MaximumUnixMilliseconds = 253_402_300_799_999;
    private const long OutsideMinimumUnixMilliseconds = MinimumUnixMilliseconds - 1;
    private const long OutsideMaximumUnixMilliseconds = MaximumUnixMilliseconds + 1;
    private const string InvalidUtf16Address = "\uD800";
    private const string NonCanonicalAddress = "127.0.0.1:11111";
    private const string EmptyEncodingMessage = "Rejected wire messages must not expose partial bytes.";

    [Test]
    public async Task AcCache014OnlyTheFourExactFlatRejectionsCanBeEncodedWithoutSigning()
    {
        ICacheControlMessage[] flatReplies =
        [
            new CachePrepareReply(null, CacheControlStatus.Rejected, null, default),
            new CacheGrantReply(null, CacheControlStatus.Rejected, Guid.Empty, null, 0, default),
            new CacheRevokeReply(null, CacheControlStatus.Rejected, Guid.Empty, CacheRevokeEffect.None, default),
            new CacheRefreshReceipt(null, CacheControlStatus.Rejected, Guid.Empty, Guid.Empty, null, default)
        ];

        foreach (var message in flatReplies)
        {
            var encoded = CacheControlWire.TryEncodeSigned(message, out var bytes);
            await Assert.That(encoded).IsTrue();
            await Assert.That(bytes.Length > 0).IsTrue();

            var signedTranscript = CacheControlWire.TryEncodeForSigning(message, out var signingBytes);
            await Assert.That(signedTranscript).IsFalse();
            await Assert.That(signingBytes.Length).IsEqualTo(0);
        }
    }

    [Test]
    public async Task AcCache014OnlyExactFlatRejectionShapesAreUnauthenticated()
    {
        ICacheControlMessage[] changedReplies =
        [
            new CachePrepareReply(null, CacheControlStatus.Busy, null, default),
            new CachePrepareReply(null, CacheControlStatus.Rejected, ReadyProof(), default),
            new CacheGrantReply(null, CacheControlStatus.Rejected, NonemptyGrantId, null, 0, default),
            new CacheGrantReply(null, CacheControlStatus.Rejected, Guid.Empty, Binding(CacheVoterSlot.Slot0), 0, default),
            new CacheGrantReply(null, CacheControlStatus.Rejected, Guid.Empty, null, 1, default),
            new CacheRevokeReply(null, CacheControlStatus.Rejected, NonemptyGrantId, CacheRevokeEffect.None, default),
            new CacheRevokeReply(null, CacheControlStatus.Rejected, Guid.Empty, CacheRevokeEffect.PendingRemoved, default),
            new CacheRefreshReceipt(null, CacheControlStatus.Rejected, NonemptyGrantId, Guid.Empty, null, default),
            new CacheRefreshReceipt(null, CacheControlStatus.Rejected, Guid.Empty, NonemptyGrantId, null, default),
            new CacheRefreshReceipt(null, CacheControlStatus.Rejected, Guid.Empty, Guid.Empty, Address(CacheVoterSlot.Slot0), default)
        ];

        foreach (var message in changedReplies)
        {
            var encoded = CacheControlWire.TryEncodeSigned(message, out var bytes);
            await Assert.That(encoded).IsFalse();
            await Assert.That(bytes.Length).IsEqualTo(0);
        }
    }

    [Test]
    public async Task AcCache014MalformedHeadersAndPhysicalAddressesFailWithNoPartialOutput()
    {
        var request = PrepareRequest();
        ICacheControlMessage[] invalidMessages =
        [
            request with { Header = request.Header with { Version = 2 } },
            request with { Header = request.Header with { Operation = CacheControlOperation.Grant } },
            request with { Header = request.Header with { ScopeHash = default } },
            request with { Header = request.Header with { PolicyHash = default } },
            request with { Header = request.Header with { PolicyRevision = 0 } },
            request with { Header = request.Header with { RequestNonce = Guid.Empty } },
            request with { Header = request.Header with { TargetSlot = null } },
            request with { Header = request.Header with { CoordinatorSessionId = Guid.Empty } },
            request with { Header = request.Header with { RoundNonce = Guid.Empty } },
            request with { Header = request.Header with { SentUnixMilliseconds = OutsideMinimumUnixMilliseconds } },
            request with { Header = request.Header with { SentUnixMilliseconds = OutsideMaximumUnixMilliseconds } },
            request with { ExpectedTargetSiloAddress = null! },
            request with { ExpectedTargetSiloAddress = InvalidUtf16Address },
            request with { ExpectedTargetSiloAddress = NonCanonicalAddress },
            request with { Header = request.Header with { OriginSlot = (CacheVoterSlot)3 } }
        ];

        foreach (var message in invalidMessages)
        {
            var encoded = CacheControlWire.TryEncodeSigned(message, out var bytes);
            await Assert.That(encoded).IsFalse();
            await Assert.That(bytes.Length).IsEqualTo(0).Because(EmptyEncodingMessage);
        }
    }

    [Test]
    public async Task AcCache014RefreshSlotsAndUnknownByteEnumsRejectBeforeEncoding()
    {
        var valid = new CacheRefreshHint(RefreshHeader(), Binding(CacheVoterSlot.Slot0), Digest(0x50));
        ICacheControlMessage[] invalidMessages =
        [
            valid with { Header = valid.Header with { TargetSlot = CacheVoterSlot.Slot0 } },
            valid with { SenderBinding = Binding(CacheVoterSlot.Slot1) },
            valid with { Header = valid.Header with { Operation = (CacheControlOperation)9 } },
            valid with { SenderBinding = valid.SenderBinding with { Role = (CachePhysicalRole)2 } }
        ];

        foreach (var message in invalidMessages)
        {
            var encoded = CacheControlWire.TryEncodeSigned(message, out var bytes);
            await Assert.That(encoded).IsFalse();
            await Assert.That(bytes.Length).IsEqualTo(0);
        }

        var validRequest = PrepareRequest();
        var canonical = CacheControlWire.TryEncodeForSigning(validRequest, out var transcript);
        await Assert.That(canonical).IsTrue();
        await Assert.That(transcript.Length > 0).IsTrue();
    }

    [Test]
    public async Task AcCache014GrantRequiresTheFixedThreeSlotCohortAndExactTargetProof()
    {
        var grant = CacheControlWireTestData.GrantRequest();
        var distinctGeneration = grant with
        {
            Slot1Proof = grant.Slot1Proof with
            {
                Binding = grant.Slot1Proof.Binding with
                {
                    SiloAddress = SiloAddress.New(IPAddress.Parse("127.0.0.2"), 11112, 8).ToParsableString()
                }
            }
        };
        ICacheControlMessage[] invalid =
        [
            grant with { GrantId = Guid.Empty },
            grant with { TargetBinding = CacheControlWireTestData.Binding(CacheVoterSlot.Slot1) },
            grant with { Slot0Proof = grant.Slot0Proof with { Binding = grant.Slot1Proof.Binding } },
            grant with { Slot1Proof = grant.Slot1Proof with { Binding = grant.Slot1Proof.Binding with { NodeId = grant.Slot0Proof.Binding.NodeId } } },
            grant with { Slot2Proof = grant.Slot2Proof with { Binding = grant.Slot2Proof.Binding with { Incarnation = DifferentIncarnationId } } },
            grant with { Slot2Proof = grant.Slot2Proof with { RoundNonce = DifferentRoundNonce } },
            grant with { Slot1Proof = null! }
        ];

        var distinctGenerationEncoded = CacheControlWire.TryEncodeSigned(distinctGeneration, out var distinctGenerationBytes);
        await Assert.That(distinctGenerationEncoded).IsTrue();
        await Assert.That(distinctGenerationBytes.Length > 0).IsTrue();
        foreach (var changed in invalid)
        {
            var encoded = CacheControlWire.TryEncodeSigned(changed, out var bytes);
            await Assert.That(encoded).IsFalse();
            await Assert.That(bytes.Length).IsEqualTo(0);
        }
    }

    private static CachePrepareRequest PrepareRequest()
        => new(Header(CacheControlOperation.Prepare, CacheVoterSlot.Slot0, CacheVoterSlot.Slot1),
            Address(CacheVoterSlot.Slot1), Digest(0x70));

    private static CacheControlHeader Header(CacheControlOperation operation, CacheVoterSlot origin, CacheVoterSlot target)
        => new(WireVersion, operation, Digest(0x10), Digest(0x30), PolicyRevision,
            origin, target, SessionId, RoundNonce, RequestNonce, ValidTimestamp);

    private static CacheControlHeader RefreshHeader()
        => new(WireVersion, CacheControlOperation.Refresh, Digest(0x10), Digest(0x30), PolicyRevision,
            CacheVoterSlot.Slot0, null, Guid.Empty, Guid.Empty, RequestNonce, ValidTimestamp);

    private static CacheReadyProof ReadyProof()
        => new(WireVersion, Digest(0x10), Digest(0x30), PolicyRevision, CacheVoterSlot.Slot0,
            SessionId, RoundNonce, ChallengeId, ChallengeSequence, Binding(CacheVoterSlot.Slot1),
            CacheControlStatus.Ready, Digest(0x70));

    private static CachePhysicalBinding Binding(CacheVoterSlot slot)
        => new(slot, GuidFor(slot, 1), IncarnationId, Address(slot), GuidFor(slot, 3), CachePhysicalRole.Canonical);

    private static string Address(CacheVoterSlot slot)
        => SiloAddress.New(IPAddress.Parse($"127.0.0.{(int)slot + 1}"), 11111 + (int)slot, 1).ToParsableString();

    private static CacheControlDigest Digest(byte firstByte)
    {
        Span<byte> bytes = stackalloc byte[32];
        bytes.Clear();
        bytes[0] = firstByte;
        bytes[31] = (byte)(firstByte + 1);
        return CacheControlDigest.FromBytes(bytes);
    }

    private static Guid GuidFor(CacheVoterSlot slot, int suffix)
        => Guid.Parse($"00000000-0000-000{(int)slot}-0000-000000000{suffix:000}");

    private static readonly Guid IncarnationId = Guid.Parse("00000000-0000-0000-0000-000000000210");
    private static readonly Guid SessionId = Guid.Parse("00000000-0000-0000-0000-000000000201");
    private static readonly Guid RoundNonce = Guid.Parse("00000000-0000-0000-0000-000000000202");
    private static readonly Guid RequestNonce = Guid.Parse("00000000-0000-0000-0000-000000000203");
    private static readonly Guid NonemptyGrantId = Guid.Parse("00000000-0000-0000-0000-000000000204");
    private static readonly Guid ChallengeId = Guid.Parse("00000000-0000-0000-0000-000000000205");
    private static readonly Guid DifferentIncarnationId = Guid.Parse("00000000-0000-0000-0000-000000000206");
    private static readonly Guid DifferentRoundNonce = Guid.Parse("00000000-0000-0000-0000-000000000207");
    private const long ChallengeSequence = 12;
}
