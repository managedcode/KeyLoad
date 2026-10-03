using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests;

/// <summary>AC-REP-006: new signed probes reuse every original strict append header boundary.</summary>
internal sealed class ReplicaReadProbeHeaderTests
{
    /// <summary>Missing required headers and invalid numeric relationships preserve both pools.</summary>
    [Test]
    [Arguments(NativeReadProbeFault.MissingTerm)]
    [Arguments(NativeReadProbeFault.MissingPreviousIndex)]
    [Arguments(NativeReadProbeFault.MissingPreviousTerm)]
    [Arguments(NativeReadProbeFault.MissingCommittedIndex)]
    [Arguments(NativeReadProbeFault.ZeroTerm)]
    [Arguments(NativeReadProbeFault.NegativePreviousIndex)]
    [Arguments(NativeReadProbeFault.ConflictingPreviousTerm)]
    [Arguments(NativeReadProbeFault.NegativeCommittedIndex)]
    public async Task StrictHeaderDenialDoesNotConsumeReadOrControlCapacity(NativeReadProbeFault fault)
    {
        using var fixture = new ReplicaSecurityFixture(new() { CriticalPerVoter = 1, ReadBarrierPerVoter = 1 });
        var value = new AppendRequest(ReplicaSecurityFixture.VoterA, 1, 0, 0, 0, []);
        var request = fixture.Request(ReplicaRpc.ReadProbe, value);
        request = fixture.Resign(request with { Payload = NativeReadProbeProducer.Encode(value, fault) });
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(request)).Code)
            .IsEqualTo(ErrorCode.Validation);
        fixture.Receiver.VerifyRequest(fixture.Read());
        fixture.Receiver.VerifyRequest(fixture.Vote());
    }

    /// <summary>R14-AC002: the same native writer without a defect admits every field and only spends the read pool.</summary>
    [Test]
    public async Task NativeReadProbeValidTwinRetainsAllFieldsAndIndependentControlCapacity()
    {
        using var fixture = new ReplicaSecurityFixture(new() { CriticalPerVoter = 1, ReadBarrierPerVoter = 1 });
        var value = new AppendRequest(ReplicaSecurityFixture.VoterA, 2, 1, 1, 1, []);
        var payload = NativeReadProbeProducer.Encode(value, NativeReadProbeFault.None);
        var restored = ReplicaProtocolCodec.DeserializeStored<AppendRequest>(payload, fixture.Configuration.MaxAppendEntries);
        await Assert.That(restored.LeaderId).IsEqualTo(value.LeaderId);
        await Assert.That(restored.Term).IsEqualTo(value.Term);
        await Assert.That(restored.PreviousIndex).IsEqualTo(value.PreviousIndex);
        await Assert.That(restored.PreviousTerm).IsEqualTo(value.PreviousTerm);
        await Assert.That(restored.CommittedIndex).IsEqualTo(value.CommittedIndex);
        await Assert.That(restored.Entries.IsDefault).IsFalse();
        await Assert.That(restored.Entries).IsEmpty();
        var request = fixture.Resign(fixture.Request(ReplicaRpc.ReadProbe, value) with { Payload = payload });
        fixture.Receiver.VerifyRequest(request);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(fixture.Read())).Code)
            .IsEqualTo(ErrorCode.ResourceExhausted);
        fixture.Receiver.VerifyRequest(fixture.Vote());
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(fixture.Vote())).Code)
            .IsEqualTo(ErrorCode.ResourceExhausted);
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
