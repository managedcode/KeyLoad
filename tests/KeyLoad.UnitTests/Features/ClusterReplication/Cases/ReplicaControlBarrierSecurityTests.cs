using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests;

/// <summary>AC-REP-006: trusted native control reads have a strict bounded signed identity.</summary>
internal sealed class ReplicaControlBarrierSecurityTests
{
    private const int OriginalSnapshotComplete = 6;
    private const int ReadProbeOrdinal = 7;
    private const int ControlReadOrdinal = 8;
    private const string Nonempty = "not-empty";
    private const string Null = "null";
    private const string Object = "{}";
    private const string DuplicateStrings = "\"\" \"\"";

    /// <summary>Existing wire ordinals remain stable and added methods follow them.</summary>
    [Test]
    public async Task AppendedMethodsPreserveExistingWireOrdinals()
    {
        var methods = Enum.GetValues<ReplicaRpc>();
        await Assert.That((int)methods.Single(method => method == ReplicaRpc.SnapshotComplete)).IsEqualTo(OriginalSnapshotComplete);
        await Assert.That((int)methods.Single(method => method == ReplicaRpc.ReadProbe)).IsEqualTo(ReadProbeOrdinal);
        await Assert.That((int)methods.Single(method => method == ReplicaRpc.ControlReadBarrier)).IsEqualTo(ControlReadOrdinal);
    }

    /// <summary>Control capacity cannot silently overflow into application capacity.</summary>
    [Test]
    public async Task FullControlPoolRejectsControlReadsButAdmitsApplicationRead()
    {
        using var fixture = new ReplicaSecurityFixture(new() { CriticalPerVoter = 1, ReadBarrierPerVoter = 1 });
        fixture.Receiver.VerifyRequest(fixture.Vote());
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(
            fixture.Request(ReplicaRpc.ControlReadBarrier, string.Empty))).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        fixture.Receiver.VerifyRequest(fixture.Read());
    }

    /// <summary>Control read payloads remain exactly one serialized empty string.</summary>
    [Test]
    [Arguments(Null)]
    [Arguments(Object)]
    [Arguments(DuplicateStrings)]
    public async Task MalformedControlPayloadCannotConsumeReserve(string payload)
    {
        using var fixture = new ReplicaSecurityFixture(new() { CriticalPerVoter = 1 });
        var request = fixture.Request(ReplicaRpc.ControlReadBarrier, string.Empty);
        request = fixture.Resign(request with { Payload = ReplicaTransportProtocol.Utf8.GetBytes(payload) });
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(request)).Code)
            .IsEqualTo(ErrorCode.Validation);
        fixture.Receiver.VerifyRequest(fixture.Vote());
    }

    /// <summary>A signed nonempty string cannot acquire control authority.</summary>
    [Test]
    public async Task NonemptyControlPayloadFailsValidation()
    {
        using var fixture = new ReplicaSecurityFixture();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(
            fixture.Request(ReplicaRpc.ControlReadBarrier, Nonempty))).Code).IsEqualTo(ErrorCode.Validation);
    }
}
