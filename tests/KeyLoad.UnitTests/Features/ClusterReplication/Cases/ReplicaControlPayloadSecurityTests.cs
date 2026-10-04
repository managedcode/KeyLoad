using System.Text;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests;

/// <summary>AC-REP-006: genuine authenticated control requests respect the configured command payload bound before nonce admission.</summary>
internal sealed class ReplicaControlPayloadSecurityTests
{
    private const string MembershipKey = "native-control-boundary";
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
        using var receiver = new ReplicaEnvelopeAuthenticator(fixture.Configuration, options, fixture.Discovery, TimeProvider.System,
            canonicalDatabase: fixture.Database);
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
        var value = new MembershipMutation(MembershipKey, 0, NativeSerialization.Serialize(0));
        var operation = fixture.Database.NormalizeOperation(new(Guid.NewGuid(), OperationKind.Membership,
            ReplicaSecurityFixture.VoterA, TimeProvider.System.GetUtcNow(), JsonSerializer.Serialize(value, JsonDefaults.Options)));
        var body = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);
        await Assert.That(body.Error).IsNull();
        var exactBytes = Math.Max(Encoding.UTF8.GetByteCount(operation.PayloadJson), body.Value.Length);
        var options = fixture.Options with
        {
            MaxControlPayloadBytes = exactBytes,
            ReplayLimits = fixture.Options.ReplayLimits with { CriticalPerVoter = ReservedCapacity }
        };
        using var receiver = new ReplicaEnvelopeAuthenticator(fixture.Configuration, options, fixture.Discovery, TimeProvider.System,
            canonicalDatabase: fixture.Database);
        var request = fixture.Request(ReplicaRpc.Forward, operation);
        await RejectOneByteShort(fixture, options, request);
        receiver.VerifyRequest(request);
        receiver.VerifyRequest(fixture.Forward(OperationKind.Batch));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => receiver.VerifyRequest(fixture.Vote())).Code)
            .IsEqualTo(ErrorCode.ResourceExhausted);
    }

    private static async Task RejectOneByteShort(ReplicaSecurityFixture fixture, ReplicaPeerOptions options, ReplicaPeerEnvelope request)
    {
        using var receiver = new ReplicaEnvelopeAuthenticator(fixture.Configuration,
            options with { MaxControlPayloadBytes = options.MaxControlPayloadBytes - 1 }, fixture.Discovery,
            TimeProvider.System, canonicalDatabase: fixture.Database);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => receiver.VerifyRequest(request));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        var reply = receiver.CreateReply(request, ReadOnlyMemory<byte>.Empty, failure.Code, failure.Message);
        fixture.Sender.VerifyReply(request, reply);
        await Assert.That(reply.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        receiver.VerifyRequest(fixture.Vote());
    }
}
