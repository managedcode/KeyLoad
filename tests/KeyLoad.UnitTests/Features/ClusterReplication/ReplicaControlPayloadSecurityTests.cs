using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests;

/// <summary>AC-REP-006: genuine authenticated control requests respect the configured command payload bound before nonce admission.</summary>
internal sealed class ReplicaControlPayloadSecurityTests
{
    private const int ReservedCapacity = 1;
    private const int ExcessPayloadBytes = 1;

    /// <summary>AC-REP-006: oversized control forwards and appends cannot spend the critical replay reserve.</summary>
    /// <param name="method">Control forward or append RPC to authenticate.</param>
    /// <param name="kind">Persisted control operation whose decoded bytes exceed the configured limit.</param>
    [Test]
    [Arguments(ReplicaRpc.Forward, OperationKind.Delivery)]
    [Arguments(ReplicaRpc.Forward, OperationKind.SubscriptionDelivery)]
    [Arguments(ReplicaRpc.Forward, OperationKind.Membership)]
    [Arguments(ReplicaRpc.Forward, OperationKind.SetDispatch)]
    [Arguments(ReplicaRpc.Append, OperationKind.Delivery)]
    [Arguments(ReplicaRpc.Append, OperationKind.SubscriptionDelivery)]
    [Arguments(ReplicaRpc.Append, OperationKind.Membership)]
    [Arguments(ReplicaRpc.Append, OperationKind.SetDispatch)]
    public async Task OversizedControlRequestReturnsSignedFailureWithoutConsumingCriticalCapacity(ReplicaRpc method, OperationKind kind)
    {
        using var fixture = new ReplicaSecurityFixture();
        var payloadBytes = ReplicaSecurityFixture.OperationPayload.Length;
        var options = fixture.Options with
        {
            MaxControlPayloadBytes = payloadBytes - ExcessPayloadBytes,
            ReplayLimits = fixture.Options.ReplayLimits with { CriticalPerVoter = ReservedCapacity }
        };
        using var receiver = new ReplicaEnvelopeAuthenticator(fixture.Configuration, options, fixture.Discovery, TimeProvider.System);
        var request = method == ReplicaRpc.Forward ? fixture.Forward(kind) : fixture.Append(kind);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => receiver.VerifyRequest(request));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        var reply = receiver.CreateReply(request, ReadOnlyMemory<byte>.Empty, failure.Code, failure.Message);
        fixture.Sender.VerifyReply(request, reply);
        await Assert.That(reply.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        receiver.VerifyRequest(fixture.Vote());
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => receiver.VerifyRequest(fixture.Vote())).Code)
            .IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>AC-REP-006: exact control bounds admit valid control payloads while the same data payload uses its independent pool.</summary>
    [Test]
    public async Task ExactDecodedControlPayloadBoundaryAndDataPoolRemainIndependent()
    {
        using var fixture = new ReplicaSecurityFixture();
        var options = fixture.Options with
        {
            MaxControlPayloadBytes = ReplicaSecurityFixture.OperationPayload.Length,
            ReplayLimits = fixture.Options.ReplayLimits with { CriticalPerVoter = ReservedCapacity }
        };
        using var receiver = new ReplicaEnvelopeAuthenticator(fixture.Configuration, options, fixture.Discovery, TimeProvider.System);
        receiver.VerifyRequest(fixture.Forward(OperationKind.Membership));
        receiver.VerifyRequest(fixture.Forward(OperationKind.Batch));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => receiver.VerifyRequest(fixture.Vote())).Code)
            .IsEqualTo(ErrorCode.ResourceExhausted);
    }
}
