using System.Collections.Immutable;
using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests;

/// <summary>AC-REP-006: authenticated application envelopes obey the append-plus-metadata byte bound before replay admission.</summary>
internal sealed class ReplicaApplicationPayloadBoundsTests
{
    private const int ReceiverAppendBytes = 16_384;
    private const int ReceiverControlPayloadBytes = ReceiverAppendBytes;
    private const int SingleSlot = 1;
    private const int OneByte = 1;
    private const int PayloadLengthStepBytes = 4;
    private const char PayloadPadding = 'x';
    private const string AdvertisedBoundFailure = "SignRequest did not accept the complete advertised payload bound.";
    private const string MetadataAllowanceFailure = "The valid application envelope did not exercise the metadata allowance.";
    private const string AppendMetadataFailure = "Could not construct an Append payload in the metadata allowance.";
    private const string MissingOperationFailure = "The application Append test requires a data operation.";

    /// <summary>Oversized application packets cannot spend nonce capacity, while bounded application and control traffic remain independent.</summary>
    /// <param name="method">Application RPC whose exact signed payload bound and replay slot are exercised.</param>
    [Test]
    [Arguments(ReplicaRpc.Forward)]
    [Arguments(ReplicaRpc.Append)]
    public async Task OversizedSignedApplicationPayloadDoesNotConsumeItsReplaySlot(ReplicaRpc method)
    {
        using var fixture = new ReplicaSecurityFixture();
        var configuration = fixture.Configuration with { MaxAppendBytes = ReceiverAppendBytes };
        var options = Options(fixture.Options);
        using var receiver = new ReplicaEnvelopeAuthenticator(UnitExecutionOptions.ReplicaConfiguration(configuration), UnitRoutingOptions.Peers(configuration, options), fixture.Discovery, TimeProvider.System, UnitRoutingOptions.Transport(), UnitRoutingOptions.Replay(options.ReplayLimits), canonicalDatabase: fixture.Database);
        using var sender = Sender(fixture, configuration, options);
        var maximumPayloadBytes = ReceiverMaximumPayloadBytes(configuration);
        await Assert.That(receiver.MaximumPayloadBytes).IsEqualTo(maximumPayloadBytes);
        await Assert.That(sender.MaximumPayloadBytes).IsEqualTo(receiver.MaximumPayloadBytes);
        await AssertSenderBoundaryAsync(fixture, sender, method, maximumPayloadBytes);

        var oversized = OversizedEnvelope(fixture, method, maximumPayloadBytes);
        await Assert.That(oversized.Payload.Length).IsGreaterThan(maximumPayloadBytes);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => receiver.VerifyRequest(oversized));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        var reply = receiver.CreateReply(oversized, ReadOnlyMemory<byte>.Empty, failure.Code, failure.Message);
        fixture.Sender.VerifyReply(oversized, reply);
        await Assert.That(reply.Error).IsEqualTo(ErrorCode.ResourceExhausted);

        receiver.VerifyRequest(WithinMetadataEnvelope(fixture, method, oversized.Nonce, configuration, maximumPayloadBytes));
        var saturated = Assert.ThrowsExactly<KeyLoadException>(() => receiver.VerifyRequest(ApplicationEnvelope(fixture, method)));
        await Assert.That(saturated.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        receiver.VerifyRequest(fixture.Vote());
        var fullControlPool = Assert.ThrowsExactly<KeyLoadException>(() => receiver.VerifyRequest(fixture.Vote()));
        await Assert.That(fullControlPool.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    private static ReplicaPeerOptions Options(ReplicaPeerOptions options) => options with
    {
        MaxControlPayloadBytes = ReceiverControlPayloadBytes,
        ReplayLimits = options.ReplayLimits with
        {
            CriticalPerVoter = SingleSlot,
            ForwardPerVoter = SingleSlot,
            ReadBarrierPerVoter = SingleSlot,
            DataAppendPerVoter = SingleSlot
        }
    };

    private static ReplicaEnvelopeAuthenticator Sender(ReplicaSecurityFixture fixture, ReplicaConfiguration receiverConfiguration,
        ReplicaPeerOptions options)
    {
        var senderConfiguration = receiverConfiguration with { LocalId = ReplicaSecurityFixture.VoterA };
        return new(UnitExecutionOptions.ReplicaConfiguration(senderConfiguration), UnitRoutingOptions.Peers(senderConfiguration, options), fixture.Discovery, TimeProvider.System, UnitRoutingOptions.Transport(), UnitRoutingOptions.Replay(options.ReplayLimits), canonicalDatabase: fixture.Database);
    }

    private static async Task AssertSenderBoundaryAsync(ReplicaSecurityFixture fixture, ReplicaEnvelopeAuthenticator sender, ReplicaRpc method,
        int maximumPayloadBytes)
    {
        var exact = sender.SignRequest(ReplicaSecurityFixture.VoterB, method, fixture.Discovery.RuntimeAddress,
            new byte[maximumPayloadBytes]);
        if (exact.Payload.Length != maximumPayloadBytes)
        {
            throw new InvalidOperationException(AdvertisedBoundFailure);
        }

        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => sender.SignRequest(ReplicaSecurityFixture.VoterB, method,
            fixture.Discovery.RuntimeAddress, new byte[maximumPayloadBytes + OneByte]));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    private static int ReceiverMaximumPayloadBytes(ReplicaConfiguration configuration) =>
        checked(configuration.MaxAppendBytes + ReplicaTransportProtocol.MaximumMetadataBytes);

    private static ReplicaPeerEnvelope OversizedEnvelope(ReplicaSecurityFixture fixture, ReplicaRpc method, int maximumPayloadBytes)
    {
        var template = ApplicationEnvelope(fixture, method);
        var payload = PayloadWithOperationSize(fixture, template, method, maximumPayloadBytes);
        return fixture.Resign(template with { Payload = payload });
    }

    private static ReplicaPeerEnvelope WithinMetadataEnvelope(ReplicaSecurityFixture fixture, ReplicaRpc method, Guid nonce,
        ReplicaConfiguration configuration, int maximumPayloadBytes)
    {
        var template = ApplicationEnvelope(fixture, method);
        var payload = method == ReplicaRpc.Forward
            ? ForwardWithPayloadLength(fixture, template, configuration.MaxAppendBytes)
            : AppendWithinEntryBound(fixture, template, configuration.MaxAppendBytes, maximumPayloadBytes);
        if (payload.Length <= configuration.MaxAppendBytes || payload.Length > maximumPayloadBytes)
        {
            throw new InvalidOperationException(MetadataAllowanceFailure);
        }
        return fixture.Resign(template with { Nonce = nonce, Payload = payload });
    }

    private static byte[] PayloadWithOperationSize(ReplicaSecurityFixture fixture, ReplicaPeerEnvelope template, ReplicaRpc method, int payloadLength)
    {
        var padding = new string(PayloadPadding, payloadLength);
        if (method == ReplicaRpc.Forward)
        {
            var operation = ReplicaProtocolCodec.Deserialize<ReplicatedOperation>(template.Payload.Span);
            return ReplicaProtocolCodec.Serialize(WithPayload(fixture, operation, padding));
        }

        var append = ReplicaProtocolCodec.Deserialize<AppendRequest>(template.Payload.Span);
        return SerializeAppend(fixture, append, padding);
    }

    private static byte[] ForwardWithPayloadLength(ReplicaSecurityFixture fixture, ReplicaPeerEnvelope template, int payloadLength)
    {
        var operation = ReplicaProtocolCodec.Deserialize<ReplicatedOperation>(template.Payload.Span);
        return ReplicaProtocolCodec.Serialize(WithPayload(fixture, operation, new string(PayloadPadding, payloadLength)));
    }

    private static byte[] AppendWithinEntryBound(ReplicaSecurityFixture fixture, ReplicaPeerEnvelope template, int maximumAppendBytes, int maximumPayloadBytes)
    {
        var append = ReplicaProtocolCodec.Deserialize<AppendRequest>(template.Payload.Span);
        var minimum = 0;
        var maximum = maximumAppendBytes / PayloadLengthStepBytes;
        byte[]? best = null;
        while (minimum <= maximum)
        {
            var payloadLength = (minimum + (maximum - minimum) / 2) * PayloadLengthStepBytes;
            var candidate = append with { Entries = AppendEntries(fixture, append, payloadLength) };
            var encodedEntries = ReplicaProtocolCodec.SerializeEntries(candidate.Entries);
            if (encodedEntries.Length <= maximumAppendBytes)
            {
                var encoded = ReplicaProtocolCodec.Serialize(candidate);
                if (encoded.Length > maximumAppendBytes && encoded.Length <= maximumPayloadBytes)
                {
                    best = encoded;
                }
                minimum = payloadLength / PayloadLengthStepBytes + 1;
            }
            else
            {
                maximum = payloadLength / PayloadLengthStepBytes - 1;
            }
        }

        return best ?? throw new InvalidOperationException(AppendMetadataFailure);
    }

    private static byte[] SerializeAppend(ReplicaSecurityFixture fixture, AppendRequest append, string payloadJson) =>
        ReplicaProtocolCodec.Serialize(append with { Entries = AppendEntries(fixture, append, payloadJson.Length, payloadJson) });

    private static ImmutableArray<ReplicaEntry> AppendEntries(ReplicaSecurityFixture fixture, AppendRequest append, int payloadLength, string? payloadJson = null)
    {
        var entries = append.Entries.ToArray();
        var operation = entries[0].Operation ?? throw new InvalidOperationException(MissingOperationFailure);
        entries[0] = entries[0] with
        {
            Operation = WithPayload(fixture, operation, payloadJson ?? new string(PayloadPadding, payloadLength))
        };
        return [.. entries];
    }

    private static ReplicatedOperation WithPayload(ReplicaSecurityFixture fixture, ReplicatedOperation operation, string payloadJson)
        => fixture.Database.NormalizeOperation(operation with { PayloadJson = payloadJson, NativePayload = ReadOnlyMemory<byte>.Empty });

    private static ReplicaPeerEnvelope ApplicationEnvelope(ReplicaSecurityFixture fixture, ReplicaRpc method) => method switch
    {
        ReplicaRpc.Forward => fixture.Forward(OperationKind.Batch),
        ReplicaRpc.Append => fixture.Append(OperationKind.Batch),
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };
}
