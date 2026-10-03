using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests;

/// <summary>AC-REP-006: new signed probes reuse every original strict append header boundary.</summary>
internal sealed class ReplicaReadProbeHeaderTests
{
    private const string Term = "\"term\":1,";
    private const string ZeroTerm = "\"term\":0,";
    private const string PreviousIndex = "\"previousIndex\":0,";
    private const string NegativeIndex = "\"previousIndex\":-1,";
    private const string PreviousTerm = "\"previousTerm\":0,";
    private const string ConflictingPreviousTerm = "\"previousTerm\":1,";
    private const string CommittedIndex = "\"committedIndex\":0,";
    private const string NegativeCommitted = "\"committedIndex\":-1,";

    /// <summary>Missing required headers and invalid numeric relationships preserve both pools.</summary>
    [Test]
    [Arguments(Term, "")]
    [Arguments(PreviousIndex, "")]
    [Arguments(PreviousTerm, "")]
    [Arguments(CommittedIndex, "")]
    [Arguments(Term, ZeroTerm)]
    [Arguments(PreviousIndex, NegativeIndex)]
    [Arguments(PreviousTerm, ConflictingPreviousTerm)]
    [Arguments(CommittedIndex, NegativeCommitted)]
    public async Task StrictHeaderDenialDoesNotConsumeReadOrControlCapacity(string field, string replacement)
    {
        using var fixture = new ReplicaSecurityFixture(new() { CriticalPerVoter = 1, ReadBarrierPerVoter = 1 });
        var request = fixture.Request(ReplicaRpc.ReadProbe,
            new AppendRequest(ReplicaSecurityFixture.VoterA, 1, 0, 0, 0, []));
        var payload = ReplicaTransportProtocol.Utf8.GetString(request.Payload.Span).Replace(field, replacement, StringComparison.Ordinal);
        request = fixture.Resign(request with { Payload = ReplicaTransportProtocol.Utf8.GetBytes(payload) });
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(request)).Code)
            .IsEqualTo(ErrorCode.Validation);
        fixture.Receiver.VerifyRequest(fixture.Read());
        fixture.Receiver.VerifyRequest(fixture.Vote());
    }

    /// <summary>A signed probe cannot overflow the receiver's next-index calculation or spend either pool.</summary>
    [Test]
    public async Task MaximumPreviousIndexFailsBeforeReplayAdmission()
    {
        using var fixture = new ReplicaSecurityFixture(new() { CriticalPerVoter = 1, ReadBarrierPerVoter = 1 });
        var request = fixture.Request(ReplicaRpc.ReadProbe,
            new AppendRequest(ReplicaSecurityFixture.VoterA, 1, long.MaxValue, 1, 0, []));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(request)).Code)
            .IsEqualTo(ErrorCode.Validation);
        fixture.Receiver.VerifyRequest(fixture.Read());
        fixture.Receiver.VerifyRequest(fixture.Vote());
    }

    /// <summary>A signed over-budget body fails before quota admission while keeping the native size classification.</summary>
    [Test]
    public async Task OversizedProbeCannotClaimAnyPool()
    {
        using var fixture = new ReplicaSecurityFixture(new() { CriticalPerVoter = 1, ReadBarrierPerVoter = 1 });
        var request = fixture.Request(ReplicaRpc.ReadProbe,
            new AppendRequest(ReplicaSecurityFixture.VoterA, 1, 0, 0, 0, []));
        request = fixture.Resign(request with { Payload = new byte[fixture.Receiver.MaximumPayloadBytes + 1] });
        var error = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(request));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(error.Message).IsEqualTo(ReplicaTransportProtocol.PayloadExceeded);
        fixture.Receiver.VerifyRequest(fixture.Read());
        fixture.Receiver.VerifyRequest(fixture.Vote());
    }
}
