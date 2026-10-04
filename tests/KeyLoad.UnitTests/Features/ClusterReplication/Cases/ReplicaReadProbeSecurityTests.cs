using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests;

/// <summary>AC-REP-006: genuine signed read probes cannot spend consensus reserve.</summary>
internal sealed class ReplicaReadProbeSecurityTests
{
    /// <summary>Both application methods share a bounded pool; native control remains independent.</summary>
    [Test]
    [Arguments(ReplicaRpc.ReadProbe)]
    [Arguments(ReplicaRpc.ReadBarrier)]
    public async Task ApplicationReadMethodsCannotConsumeNativeControlReserve(ReplicaRpc first)
    {
        using var fixture = new ReplicaSecurityFixture();
        fixture.Receiver.VerifyRequest(Request(fixture, first));
        foreach (var method in new[] { ReplicaRpc.ReadProbe, ReplicaRpc.ReadBarrier })
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
                fixture.Receiver.VerifyRequest(Request(fixture, method))).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        }
        fixture.Receiver.VerifyRequest(Request(fixture, ReplicaRpc.ControlReadBarrier));
        fixture.Receiver.VerifyRequest(fixture.Append());
        fixture.Receiver.VerifyRequest(fixture.Vote());
    }

    /// <summary>Nonempty data, control and noop appends are never admitted as probes.</summary>
    [Test]
    [Arguments(OperationKind.Batch)]
    [Arguments(OperationKind.Membership)]
    [Arguments(null)]
    public async Task NonemptyProbeFailsBeforeEitherPoolIsConsumed(OperationKind? kind)
    {
        using var fixture = new ReplicaSecurityFixture(new() { CriticalPerVoter = 1, ReadBarrierPerVoter = 1 });
        var invalid = fixture.Resign(fixture.Append(kind) with { Method = ReplicaRpc.ReadProbe });
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(invalid)).Code)
            .IsEqualTo(ErrorCode.Validation);
        fixture.Receiver.VerifyRequest(Request(fixture, ReplicaRpc.ReadProbe));
        fixture.Receiver.VerifyRequest(fixture.Vote());
    }

    /// <summary>Null, missing, duplicate, unknown and trailing fields cannot claim either pool.</summary>
    [Test]
    [Arguments(NativeReadProbeFault.NullEntries)]
    [Arguments(NativeReadProbeFault.DuplicateEntries)]
    [Arguments(NativeReadProbeFault.UnknownEntries)]
    [Arguments(NativeReadProbeFault.MissingEntries)]
    [Arguments(NativeReadProbeFault.TrailingObject)]
    public async Task MalformedSignedProbePreservesCapacity(NativeReadProbeFault fault)
    {
        using var fixture = new ReplicaSecurityFixture(new() { CriticalPerVoter = 1, ReadBarrierPerVoter = 1 });
        var value = new AppendRequest(ReplicaSecurityFixture.VoterA, 1, 0, 0, 0, []);
        var request = fixture.Request(ReplicaRpc.ReadProbe, value);
        request = fixture.Resign(request with { Payload = NativeReadProbeProducer.Encode(value, fault) });
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(request)).Code)
            .IsEqualTo(ErrorCode.Validation);
        fixture.Receiver.VerifyRequest(Request(fixture, ReplicaRpc.ReadProbe));
        fixture.Receiver.VerifyRequest(fixture.Vote());
    }

    /// <summary>Retagging needs a fresh MAC, and the declared leader must be the authenticated sender.</summary>
    [Test]
    public async Task ProbeRetainsExactMethodMacAndDeclaredLeaderBinding()
    {
        using var fixture = new ReplicaSecurityFixture();
        var retagged = fixture.Append() with { Method = ReplicaRpc.ReadProbe };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(retagged)).Code)
            .IsEqualTo(ErrorCode.Unauthenticated);
        var mismatch = fixture.Request(ReplicaRpc.ReadProbe,
            new AppendRequest(ReplicaSecurityFixture.VoterC, 1, 0, 0, 0, []));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(mismatch)).Code)
            .IsEqualTo(ErrorCode.PermissionDenied);
        fixture.Receiver.VerifyRequest(Request(fixture, ReplicaRpc.ReadProbe));
    }

    /// <summary>Nonces remain unique across application/control methods even when a pool is full.</summary>
    [Test]
    public async Task NonceCannotMoveBetweenReadProbeAndControlMethods()
    {
        using var fixture = new ReplicaSecurityFixture();
        var original = Request(fixture, ReplicaRpc.ReadProbe);
        fixture.Receiver.VerifyRequest(original);
        foreach (var method in new[] { ReplicaRpc.ReadBarrier, ReplicaRpc.ControlReadBarrier, ReplicaRpc.Append })
        {
            var replay = fixture.Resign(Request(fixture, method) with { Nonce = original.Nonce });
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(replay)).Code)
                .IsEqualTo(ErrorCode.Unauthenticated);
        }
    }

    private static ReplicaPeerEnvelope Request(ReplicaSecurityFixture fixture, ReplicaRpc method)
        => method is ReplicaRpc.ReadProbe or ReplicaRpc.Append
            ? fixture.Request(method, new AppendRequest(ReplicaSecurityFixture.VoterA, 1, 0, 0, 0, []))
            : fixture.Request(method, string.Empty);
}
