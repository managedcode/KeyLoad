using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PhysicalOwnerProbeNativeTestValues
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    internal static readonly TimeSpan Retention = TimeSpan.FromMinutes(2);
    internal static string InnerNonce() => Convert.ToBase64String(Guid.NewGuid().ToByteArray())
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    internal static PhysicalOwnerProbeCallV1 Call(ReplicaSecurityFixture fixture)
    {
        var control = new RegisteredPhysicalOwnerV1(new(Guid.NewGuid(), fixture.Configuration.Incarnation,
            fixture.Configuration.VoterIds, 1), fixture.Configuration.VoterIds);
        var destination = control with { Owner = control.Owner with { PhysicalShardId = Guid.NewGuid() } };
        return new(PhysicalOwnerProbeProtocol.Version, Guid.NewGuid(), control, destination,
            ReplicaSecurityFixture.VoterA, fixture.Discovery.RuntimeAddress, InnerNonce(), Now, Now + Retention);
    }
    internal static PartitionMovementGrainServiceRequest Request(ReplicaSecurityFixture fixture,
        PhysicalOwnerProbeCallV1 call, PhysicalOwnerProbeMac inner)
    {
        var payload = PhysicalOwnerProbeWire.Encode(call);
        return new(PartitionMovementGrainServiceChannel.PhysicalOwnerProbe, true, payload,
            inner.Sign(payload, reply: false), call.CallerVoter, call.CallerSiloAddress,
            ReplicaSecurityFixture.VoterB, fixture.Discovery.RuntimeAddress, Guid.NewGuid(), call.ExpiresAt, string.Empty);
    }
}
