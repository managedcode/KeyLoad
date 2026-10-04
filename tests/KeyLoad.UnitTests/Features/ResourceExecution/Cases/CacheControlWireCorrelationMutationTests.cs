using System.Net;
using KeyLoad.Orleans.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.ResourceExecution.CacheControlWireAuthenticatorSupport;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheControlWireCorrelationMutationTests
{
    [Test]
    public async Task AcCache014PrepareCorrelationEchoesEveryShapeValidHeaderField()
    {
        var request = CacheControlWireTestData.PrepareRequest();
        var correlation = Correlation(request);
        var fields = new[]
        {
            correlation.Header with { ScopeHash = CacheControlWireTestData.Digest(0x91) },
            correlation.Header with { PolicyHash = CacheControlWireTestData.Digest(0x92) },
            correlation.Header with { PolicyRevision = correlation.Header.PolicyRevision + 1 },
            correlation.Header with { OriginSlot = CacheVoterSlot.Slot1 },
            correlation.Header with { TargetSlot = CacheVoterSlot.Slot2 },
            correlation.Header with { CoordinatorSessionId = ChangedSessionId },
            correlation.Header with { RoundNonce = ChangedRoundNonce },
            correlation.Header with { RequestNonce = ChangedRequestNonce },
            correlation.Header with { SentUnixMilliseconds = correlation.Header.SentUnixMilliseconds + 1 }
        };

        foreach (var header in fields)
        {
            await AssertHeaderMismatchAsync(request, correlation, header);
        }
    }

    private static async Task AssertHeaderMismatchAsync(CachePrepareRequest request,
        CacheReplyCorrelation correlation, CacheControlHeader header)
    {
        var busy = new CachePrepareReply(correlation with { Header = header }, CacheControlStatus.Busy,
            null, CacheControlWireTestData.NondefaultMac);
        var shapeRemainsValid = CacheControlWire.TryEncodeSigned(busy, out var bytes);

        await Assert.That(shapeRemainsValid).IsTrue();
        await Assert.That(bytes.Length > 0).IsTrue();
        await Assert.That(CacheControlCorrelation.TryMatch(request, busy)).IsFalse();
    }

    [Test]
    public async Task AcCache014AcceptedBindingMatchesExactSameSlotPhysicalIdentityAndAddress()
    {
        var request = CacheControlWireTestData.GrantRequest();
        var correlation = Correlation(request);
        var accepted = new CacheGrantReply(correlation, CacheControlStatus.AcceptedActive,
            request.GrantId, request.TargetBinding, request.Slot2Proof.ChallengeSequence,
            CacheControlWireTestData.NondefaultMac);
        var acceptedCold = accepted with { Status = CacheControlStatus.AcceptedCold };
        var binding = request.TargetBinding;
        var wrong = SameSlotBindingMismatches(accepted, binding);

        await Assert.That(CacheControlCorrelation.TryMatch(request, accepted)).IsTrue();
        await Assert.That(CacheControlCorrelation.TryMatch(request, acceptedCold)).IsTrue();
        foreach (var changed in wrong)
        {
            var remainsShapeValid = CacheControlWire.TryEncodeSigned(changed, out var bytes);
            await Assert.That(remainsShapeValid).IsTrue();
            await Assert.That(bytes.Length > 0).IsTrue();
            await Assert.That(CacheControlCorrelation.TryMatch(request, changed)).IsFalse();
        }

        var changedGrant = accepted with { GrantId = DifferentGrantId };
        var revoke = CacheControlWireTestData.RevokeRequest();
        var revokeReply = new CacheRevokeReply(Correlation(revoke), CacheControlStatus.Revoked,
            revoke.GrantId, CacheRevokeEffect.Both, CacheControlWireTestData.NondefaultMac);
        var changedRevoke = revokeReply with { GrantId = DifferentGrantId };
        var changedProof = new CachePrepareReply(Correlation(CacheControlWireTestData.PrepareRequest()),
            CacheControlStatus.Ready,
            CacheControlWireTestData.Proof(CacheVoterSlot.Slot0, CacheVoterSlot.Slot1) with
            {
                Binding = CacheControlWireTestData.Binding(CacheVoterSlot.Slot1) with
                {
                    SiloAddress = SiloAddress.New(IPAddress.Loopback, 11118, 7).ToParsableString()
                }
            }, CacheControlWireTestData.NondefaultMac);
        var prepare = CacheControlWireTestData.PrepareRequest();

        await AssertShapeValidMismatchAsync(request, changedGrant);
        await AssertShapeValidMismatchAsync(revoke, changedRevoke);
        await AssertShapeValidMismatchAsync(prepare, changedProof);
    }

    private static async Task AssertShapeValidMismatchAsync(ICacheControlRequest request, ICacheControlReply reply)
    {
        var shapeRemainsValid = CacheControlWire.TryEncodeSigned(reply, out var bytes);
        await Assert.That(shapeRemainsValid).IsTrue();
        await Assert.That(bytes.Length > 0).IsTrue();
        await Assert.That(CacheControlCorrelation.TryMatch(request, reply)).IsFalse();
    }

    private static CacheGrantReply[] SameSlotBindingMismatches(CacheGrantReply accepted, CachePhysicalBinding binding)
        =>
        [
            accepted with { AcceptedBinding = binding with { NodeId = ChangedNodeId } },
            accepted with { AcceptedBinding = binding with { Incarnation = ChangedIncarnation } },
            accepted with { AcceptedBinding = binding with { RuntimeId = ChangedRuntimeId } },
            accepted with
            {
                AcceptedBinding = binding with
                {
                    SiloAddress = SiloAddress.New(IPAddress.Loopback, 11119, 7).ToParsableString()
                }
            }
        ];

    private static readonly Guid ChangedSessionId = Guid.Parse("a1234567-89ab-cdef-8012-3456789abcde");
    private static readonly Guid ChangedRoundNonce = Guid.Parse("b1234567-89ab-cdef-8012-3456789abcdf");
    private static readonly Guid ChangedRequestNonce = Guid.Parse("b1234567-89ab-cdef-8012-3456789abce0");
    private static readonly Guid ChangedNodeId = Guid.Parse("c1234567-89ab-cdef-8012-3456789abcde");
    private static readonly Guid ChangedIncarnation = Guid.Parse("d1234567-89ab-cdef-8012-3456789abcde");
    private static readonly Guid ChangedRuntimeId = Guid.Parse("e1234567-89ab-cdef-8012-3456789abcde");
    private static readonly Guid DifferentGrantId = Guid.Parse("f1234567-89ab-cdef-8012-3456789abcde");
}
