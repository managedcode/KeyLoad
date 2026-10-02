using System.Collections.Immutable;
using System.Net;
using System.Security.Cryptography;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;

namespace KeyLoad.UnitTests;

/// <summary>AC-REP-006: genuine MACs bind peer scope, method, bytes, runtime generation and nonce.</summary>
internal sealed class ReplicaTransportSecurityTests
{
    /// <summary>AC-REP-006: changed request fields cannot pass an unchanged genuine signature.</summary>
    [Test]
    [Arguments(ReplicaSecurityMutation.Payload)]
    [Arguments(ReplicaSecurityMutation.Method)]
    [Arguments(ReplicaSecurityMutation.Nonce)]
    [Arguments(ReplicaSecurityMutation.Generation)]
    [Arguments(ReplicaSecurityMutation.Incarnation)]
    [Arguments(ReplicaSecurityMutation.Voter)]
    public async Task RequestTamperOrWrongScopeCannotConsumeReservedCapacity(ReplicaSecurityMutation mutation)
    {
        using var fixture = new ReplicaSecurityFixture();
        var request = fixture.Vote();
        var changed = mutation switch
        {
            ReplicaSecurityMutation.Payload => request with { Payload = ReplicaSecurityFixture.EmptyRead },
            ReplicaSecurityMutation.Method => request with { Method = ReplicaRpc.ReadBarrier },
            ReplicaSecurityMutation.Nonce => request with { Nonce = Guid.NewGuid() },
            ReplicaSecurityMutation.Generation => request with { RuntimeAddress = fixture.OtherGeneration },
            ReplicaSecurityMutation.Incarnation => request with { Incarnation = Guid.NewGuid() },
            _ => request with { Sender = ReplicaSecurityFixture.UnknownVoter }
        };
        if (mutation is ReplicaSecurityMutation.Generation or ReplicaSecurityMutation.Incarnation or ReplicaSecurityMutation.Voter)
        {
            changed = fixture.Resign(changed);
        }
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(changed)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        fixture.Receiver.VerifyRequest(request);
    }

    /// <summary>AC-REP-006: errors are signed and their code, bytes, request nonce and generation cannot be substituted.</summary>
    [Test]
    [Arguments(ReplicaSecurityMutation.Payload)]
    [Arguments(ReplicaSecurityMutation.Method)]
    [Arguments(ReplicaSecurityMutation.Nonce)]
    [Arguments(ReplicaSecurityMutation.Generation)]
    [Arguments(ReplicaSecurityMutation.Incarnation)]
    [Arguments(ReplicaSecurityMutation.Voter)]
    [Arguments(ReplicaSecurityMutation.Error)]
    public async Task SignedCapacityFailureIsBoundToItsOriginatingRequest(ReplicaSecurityMutation mutation)
    {
        using var fixture = new ReplicaSecurityFixture();
        fixture.Receiver.VerifyRequest(fixture.Forward(OperationKind.Batch));
        var request = fixture.Forward(OperationKind.Batch);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(request));
        var reply = fixture.Receiver.CreateReply(request, ReadOnlyMemory<byte>.Empty, failure.Code, failure.Message);
        fixture.Sender.VerifyReply(request, reply);
        await Assert.That(reply.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        var changed = mutation switch
        {
            ReplicaSecurityMutation.Payload => reply with { Payload = ReplicaSecurityFixture.EmptyRead },
            ReplicaSecurityMutation.Method => reply with { Method = ReplicaRpc.ReadBarrier },
            ReplicaSecurityMutation.Nonce => reply with { RequestNonce = Guid.NewGuid() },
            ReplicaSecurityMutation.Generation => reply with { RuntimeAddress = fixture.OtherGeneration },
            ReplicaSecurityMutation.Incarnation => reply with { Incarnation = Guid.NewGuid() },
            ReplicaSecurityMutation.Voter => reply with { Sender = ReplicaSecurityFixture.UnknownVoter },
            _ => reply with { Error = ErrorCode.PermissionDenied }
        };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Sender.VerifyReply(request, changed)).Code).IsEqualTo(ErrorCode.Unauthenticated);
    }

    /// <summary>AC-REP-006: discovery MACs reject substituted bytes, voters and GET nonces.</summary>
    [Test]
    public async Task DiscoverySignatureBindsExactBytesExpectedVoterAndRequestNonce()
    {
        using var fixture = new ReplicaSecurityFixture();
        var nonce = Guid.NewGuid();
        var bytes = ReplicaProtocolCodec.Serialize(fixture.Discovery.Read());
        var signature = fixture.Receiver.SignDiscovery(bytes, nonce);
        fixture.Sender.VerifyDiscovery(ReplicaSecurityFixture.VoterB, bytes, nonce, signature);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Sender.VerifyDiscovery(
            ReplicaSecurityFixture.VoterB, bytes, Guid.NewGuid(), signature)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Sender.VerifyDiscovery(
            ReplicaSecurityFixture.VoterC, bytes, nonce, signature)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Sender.VerifyDiscovery(
            ReplicaSecurityFixture.VoterB, ReplicaSecurityFixture.EmptyRead, nonce, signature)).Code).IsEqualTo(ErrorCode.Unauthenticated);
    }

    /// <summary>AC-REP-006: a different real credential or cluster scope cannot verify a genuine envelope.</summary>
    [Test]
    public async Task DifferentCredentialOrClusterIdentityCannotVerifyRequest()
    {
        using var fixture = new ReplicaSecurityFixture();
        var request = fixture.Vote();
        var credential = RandomNumberGenerator.GetBytes(ReplicaTransportProtocol.SecretBytes);
        var options = fixture.Options with { Secret = credential };
        using var wrongCredential = new ReplicaEnvelopeAuthenticator(fixture.Configuration, options, fixture.Discovery, TimeProvider.System);
        using var wrongCluster = new ReplicaEnvelopeAuthenticator(fixture.Configuration,
            fixture.Options with { ClusterId = ReplicaSecurityFixture.OtherClusterId }, fixture.Discovery, TimeProvider.System);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => wrongCredential.VerifyRequest(request)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => wrongCluster.VerifyRequest(request)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        CryptographicOperations.ZeroMemory(credential);
    }
}

/// <summary>AC-REP-006: application saturation cannot spend the fixed-voter consensus reserve.</summary>
internal sealed class ReplicaReplayCapacityTests
{
    /// <summary>AC-REP-006: full forward/read/append pools still admit votes, snapshots, noops and control operations.</summary>
    [Test]
    public async Task SaturatedApplicationPoolsPreserveIndependentConsensusAndControlCapacity()
    {
        using var fixture = new ReplicaSecurityFixture();
        foreach (var request in new[] { fixture.Forward(OperationKind.Batch), fixture.Read(), fixture.Append(OperationKind.Batch) })
        {
            fixture.Receiver.VerifyRequest(request);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(
                fixture.Resign(request with { Nonce = Guid.NewGuid() }))).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        }
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(
            fixture.Append(OperationKind.Membership, OperationKind.Batch))).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        fixture.Receiver.VerifyRequest(fixture.Vote());
        fixture.Receiver.VerifyRequest(fixture.Append());
        fixture.Receiver.VerifyRequest(fixture.Append((OperationKind?)null));
        foreach (var kind in new[] { OperationKind.Delivery, OperationKind.SubscriptionDelivery, OperationKind.Membership, OperationKind.SetDispatch })
        {
            fixture.Receiver.VerifyRequest(fixture.Forward(kind));
            fixture.Receiver.VerifyRequest(fixture.Append(kind));
        }
        var transferId = Guid.NewGuid();
        var image = new ReplicaSnapshot(transferId, fixture.Configuration.Incarnation, 1, 1, 1,
            Convert.ToHexStringLower(SHA256.HashData([ReplicaSecurityFixture.SnapshotByte])),
            transferId.ToString(ReplicaTransportProtocol.NonceFormat) + ReplicaProtocol.SnapshotExtension);
        fixture.Receiver.VerifyRequest(fixture.Request(ReplicaRpc.SnapshotBegin, new SnapshotBeginRequest(ReplicaSecurityFixture.VoterA, 1, image)));
        fixture.Receiver.VerifyRequest(fixture.Request(ReplicaRpc.SnapshotChunk,
            new SnapshotChunkRequest(ReplicaSecurityFixture.VoterA, 1, image.TransferId, 0, new byte[] { ReplicaSecurityFixture.SnapshotByte })));
        fixture.Receiver.VerifyRequest(fixture.Request(ReplicaRpc.SnapshotComplete,
            new SnapshotCompleteRequest(ReplicaSecurityFixture.VoterA, 1, image.TransferId)));
    }

    /// <summary>AC-REP-006: a properly resigned cross-method reuse remains authentication failure even when data is full.</summary>
    [Test]
    public async Task NonceReuseCannotMoveBetweenApplicationAndCriticalMethods()
    {
        using var fixture = new ReplicaSecurityFixture();
        var original = fixture.Forward(OperationKind.Batch);
        fixture.Receiver.VerifyRequest(original);
        var read = fixture.Resign(fixture.Read() with { Nonce = original.Nonce });
        var vote = fixture.Resign(fixture.Vote() with { Nonce = original.Nonce });
        var malformed = fixture.Resign(original with { Payload = ReplicaTransportProtocol.Utf8.GetBytes(ReplicaSecurityFixture.MissingOperationFields) });
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(read)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(vote)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(original)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(malformed)).Code).IsEqualTo(ErrorCode.Unauthenticated);
    }

    /// <summary>AC-REP-006: one fixed peer cannot consume another peer's data or consensus capacity.</summary>
    [Test]
    public async Task FixedVoterCapacityIsIsolatedAndEveryCriticalPoolIsBounded()
    {
        using var fixture = new ReplicaSecurityFixture(new() { CriticalPerVoter = 1, ForwardPerVoter = 1, ReadBarrierPerVoter = 1, DataAppendPerVoter = 1 });
        fixture.Receiver.VerifyRequest(fixture.Forward(OperationKind.Batch));
        fixture.Receiver.VerifyRequest(fixture.Resign(fixture.Forward(OperationKind.Batch) with { Sender = ReplicaSecurityFixture.VoterC }));
        fixture.Receiver.VerifyRequest(fixture.Vote());
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(fixture.Vote())).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        fixture.Receiver.VerifyRequest(fixture.Request(ReplicaRpc.RequestVote, new VoteRequest(ReplicaSecurityFixture.VoterC, 1, 0, 0), ReplicaSecurityFixture.VoterC));
    }

    /// <summary>AC-REP-006: duplicate, unknown, missing and invalid classifier fields cannot claim control capacity.</summary>
    [Test]
    [Arguments(ReplicaSecurityFixture.DuplicateKind)]
    [Arguments(ReplicaSecurityFixture.UnknownKind)]
    [Arguments(ReplicaSecurityFixture.MissingOperationFields)]
    [Arguments(ReplicaSecurityFixture.InvalidPayload)]
    [Arguments(ReplicaSecurityFixture.UnknownField)]
    public async Task MalformedSignedControlPayloadDoesNotConsumeReserve(string scenario)
    {
        using var fixture = new ReplicaSecurityFixture(new() { CriticalPerVoter = 1 });
        var invalid = fixture.InvalidControl(scenario);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(invalid)).Code).IsEqualTo(ErrorCode.Validation);
        fixture.Receiver.VerifyRequest(fixture.Vote());
    }

    /// <summary>AC-REP-006: duplicate append entries or operations cannot masquerade as an empty/noop append.</summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task DuplicateAppendFieldsCannotConsumeCriticalCapacity(bool operation)
    {
        using var fixture = new ReplicaSecurityFixture(new() { CriticalPerVoter = 1 });
        var request = fixture.Append(OperationKind.Membership);
        var source = ReplicaTransportProtocol.Utf8.GetString(request.Payload.Span);
        var changed = operation ? source.Replace(ReplicaSecurityFixture.OperationToken, ReplicaSecurityFixture.DuplicateOperationToken, StringComparison.Ordinal)
            : source.Replace(ReplicaSecurityFixture.EntriesToken, ReplicaSecurityFixture.DuplicateEntriesToken, StringComparison.Ordinal);
        var invalid = fixture.Resign(request with { Payload = ReplicaTransportProtocol.Utf8.GetBytes(changed) });
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(invalid)).Code).IsEqualTo(ErrorCode.Validation);
        fixture.Receiver.VerifyRequest(fixture.Vote());
    }

    /// <summary>AC-REP-006: genuine concurrent verifications admit one nonce exactly once.</summary>
    [Test]
    public async Task ConcurrentNonceReuseAdmitsExactlyOneRequest()
    {
        using var fixture = new ReplicaSecurityFixture();
        var request = fixture.Vote();
        var results = await Task.WhenAll(Enumerable.Range(0, ReplicaSecurityFixture.ReservedCapacity).Select(_ => Task.Run(() =>
        {
            try
            { fixture.Receiver.VerifyRequest(request); return (ErrorCode?)null; }
            catch (KeyLoadException error) { return error.Code; }
        })));
        await Assert.That(results.Count(result => result is null)).IsEqualTo(1);
        await Assert.That(results.Count(result => result == ErrorCode.Unauthenticated)).IsEqualTo(ReplicaSecurityFixture.ReservedCapacity - 1);
    }

    /// <summary>AC-REP-006: escaped base64 remains valid wire JSON without allocating a decoded payload to classify it.</summary>
    [Test]
    public async Task ValidEscapedBase64RetainsControlClassificationAndOriginalOperationBytes()
    {
        using var fixture = new ReplicaSecurityFixture();
        var request = fixture.Forward(OperationKind.Membership);
        var payload = ReplicaTransportProtocol.Utf8.GetString(request.Payload.Span).Replace(ReplicaSecurityFixture.PayloadToken,
            ReplicaSecurityFixture.EscapedPayloadToken, StringComparison.Ordinal);
        request = fixture.Resign(request with { Payload = ReplicaTransportProtocol.Utf8.GetBytes(payload) });
        fixture.Receiver.VerifyRequest(request);
        await Assert.That(ReplicaProtocolCodec.Deserialize<ReplicatedOperation>(request.Payload.Span).PayloadJson).IsEqualTo(ReplicaSecurityFixture.OperationPayload);
    }

    /// <summary>AC-REP-006: maximum operation classification scans encoded bytes without allocating the decoded command.</summary>
    [Test]
    public async Task MaximumOperationClassificationDoesNotAllocateItsDecodedPayload()
    {
        using var fixture = new ReplicaSecurityFixture();
        var limits = new DatabaseLimits();
        var payload = JsonDefaults.Serialize(new string(ReplicaSecurityFixture.PayloadPadding, limits.MaxBatchBytes - ReplicaSecurityFixture.JsonStringQuoteBytes));
        var operation = new ReplicatedOperation(Guid.NewGuid(), OperationKind.Batch, ReplicaSecurityFixture.VoterA,
            TimeProvider.System.GetUtcNow(), ReplicaTransportProtocol.Utf8.GetString(payload));
        var request = fixture.Request(ReplicaRpc.Forward, operation);
        var before = GC.GetAllocatedBytesForCurrentThread();
        fixture.Receiver.VerifyRequest(request);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(allocated).IsLessThan(ReplicaSecurityFixture.MaximumClassificationAllocationBytes);
    }
}

/// <summary>AC-REP-006: fixed capacities and inclusive lifetime bounds preserve replay protection.</summary>
internal sealed class ReplicaReplayLifetimeTests
{
    /// <summary>AC-REP-006: expiry and future timestamp limits use the real system clock and genuine signatures.</summary>
    [Test]
    public async Task StaleAndTooFutureEnvelopesFailWhileAllowedFutureNonceStaysRetained()
    {
        using var fixture = new ReplicaSecurityFixture();
        var now = TimeProvider.System.GetUtcNow().ToUnixTimeMilliseconds();
        var lifetime = checked((long)ReplicaTransportProtocol.EnvelopeLifetime.TotalMilliseconds);
        var request = fixture.Vote();
        var expired = fixture.Resign(request with { Timestamp = now - lifetime - ReplicaSecurityFixture.TimeMarginMilliseconds });
        var future = fixture.Resign(request with { Timestamp = now + lifetime + ReplicaSecurityFixture.TimeMarginMilliseconds });
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(expired)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(future)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        var accepted = fixture.Resign(request with { Timestamp = now + lifetime / 2 });
        fixture.Receiver.VerifyRequest(accepted);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(accepted)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        var window = new ReplicaReplayWindow(fixture.Configuration.VoterIds, fixture.Options.ReplayLimits);
        window.Admit(accepted.Sender, accepted.Nonce, accepted.Timestamp, now, ReplicaReplayPool.Critical);
        var expiry = accepted.Timestamp + lifetime;
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => window.Admit(accepted.Sender, accepted.Nonce,
            expiry, expiry, ReplicaReplayPool.Forward)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        window.Admit(accepted.Sender, accepted.Nonce, expiry + 1, expiry + 1, ReplicaReplayPool.Forward);
    }

    /// <summary>AC-REP-006: zero and oversized configured capacities fail before transport publication.</summary>
    [Test]
    public async Task ReplayConfigurationRejectsNonpositiveAndUnboundedMemoryBudgets()
    {
        using var fixture = new ReplicaSecurityFixture();
        var zero = fixture.Options with { ReplayLimits = new() { CriticalPerVoter = 0 } };
        var oversized = fixture.Options with { ReplayLimits = new() { ForwardPerVoter = int.MaxValue } };
        Assert.ThrowsExactly<InvalidOperationException>(() => zero.Validate(fixture.Configuration));
        Assert.ThrowsExactly<InvalidOperationException>(() => oversized.Validate(fixture.Configuration));
        await Assert.That(fixture.Options.ReplayLimits.MaximumRetainedNonces(fixture.Configuration.VoterIds.Length))
            .IsEqualTo(ReplicaSecurityFixture.ReservedCapacity * fixture.Configuration.VoterIds.Length + fixture.Configuration.VoterIds.Length * 3);
    }
}

/// <summary>Cases for authenticated request/reply boundary mutation.</summary>
internal enum ReplicaSecurityMutation
{
    /// <summary>Exact payload bytes.</summary>
    Payload,
    /// <summary>RPC method.</summary>
    Method,
    /// <summary>Request nonce.</summary>
    Nonce,
    /// <summary>Runtime address generation.</summary>
    Generation,
    /// <summary>Durable incarnation.</summary>
    Incarnation,
    /// <summary>Configured voter.</summary>
    Voter,
    /// <summary>Authenticated error code.</summary>
    Error
}

internal sealed class ReplicaSecurityFixture : IDisposable
{
    internal const string VoterA = "http://voter-a:8080";
    internal const string VoterB = "http://voter-b:8080";
    internal const string VoterC = "http://voter-c:8080";
    internal const string UnknownVoter = "http://unknown-voter:8080";
    internal const string ClusterId = "replica-transport-security";
    internal const string OtherClusterId = "other-replica-cluster";
    internal const string OperationPayload = "{}";
    internal const string DuplicateKind = "{\"kind\":\"Batch\",\"kind\":\"Membership\"}";
    internal const string UnknownKind = "{\"kind\":\"Unknown\"}";
    internal const string MissingOperationFields = "{\"kind\":\"Membership\"}";
    internal const string InvalidPayload = "{\"id\":\"00000000-0000-0000-0000-000000000001\",\"kind\":\"Membership\",\"principalId\":\"root\",\"evaluatedAt\":\"2026-10-01T00:00:00Z\",\"payloadJson\":false}";
    internal const string UnknownField = "unknown-field";
    internal const string KindToken = "\"kind\":\"Membership\"";
    internal const string PayloadToken = "\"payloadJson\":\"e30=\"";
    internal const string InvalidPayloadToken = "\"payloadJson\":false";
    internal const string EscapedPayloadToken = "\"payloadJson\":\"\\u0065\\u0033\\u0030\\u003D\"";
    internal const string UnknownFieldToken = "\"unknown\":true,\"kind\":\"Membership\"";
    internal const string OperationToken = "\"operation\":";
    internal const string DuplicateOperationToken = "\"operation\":null,\"operation\":";
    internal const string EntriesToken = "\"entries\":";
    internal const string DuplicateEntriesToken = "\"entries\":[],\"entries\":";
    internal const int SiloPort = 11_111;
    internal const int ReservedCapacity = 32;
    internal const int TimeMarginMilliseconds = 1_000;
    internal const int JsonStringQuoteBytes = 2;
    internal const long MaximumClassificationAllocationBytes = 1_048_576;
    internal const char PayloadPadding = 'x';
    internal const byte SnapshotByte = 1;
    internal static byte[] EmptyRead { get; } = ReplicaProtocolCodec.Serialize(string.Empty);
    private readonly IHost host;
    private readonly ReplicaMessageMac mac;
    private readonly byte[] credential;
    internal ReplicaConfiguration Configuration { get; }
    internal ReplicaPeerOptions Options { get; }
    internal ReplicaSiloDiscoveryState Discovery { get; }
    internal ReplicaEnvelopeAuthenticator Sender { get; }
    internal ReplicaEnvelopeAuthenticator Receiver { get; }
    internal string OtherGeneration { get; }

    internal ReplicaSecurityFixture(ReplicaReplayLimits? limits = null)
    {
        host = new HostBuilder().UseOrleans(silo => silo.Services.Configure<EndpointOptions>(endpoint =>
        { endpoint.AdvertisedIPAddress = IPAddress.Loopback; endpoint.SiloPort = SiloPort; endpoint.GatewayPort = 0; })).Build();
        var local = host.Services.GetRequiredService<ILocalSiloDetails>();
        Configuration = new(VoterB, [VoterA, VoterB, VoterC], Path.GetTempPath(), Guid.NewGuid());
        credential = RandomNumberGenerator.GetBytes(ReplicaTransportProtocol.SecretBytes);
        Options = new(new() { [VoterA] = new(VoterA), [VoterB] = new(VoterB), [VoterC] = new(VoterC) },
            credential, ClusterId)
        { ReplayLimits = limits ?? new() { CriticalPerVoter = ReservedCapacity, ForwardPerVoter = 1, ReadBarrierPerVoter = 1, DataAppendPerVoter = 1 } };
        Discovery = new(Configuration, Options, local);
        Receiver = new(Configuration, Options, Discovery, TimeProvider.System);
        var senderConfiguration = Configuration with { LocalId = VoterA };
        Sender = new(senderConfiguration, Options, new(senderConfiguration, Options, local), TimeProvider.System);
        OtherGeneration = SiloAddress.New(local.SiloAddress.Endpoint, checked(local.SiloAddress.Generation + 1)).ToParsableString();
        mac = new(Options.Secret, Options.ClusterId);
    }

    internal ReplicaPeerEnvelope Vote() => Request(ReplicaRpc.RequestVote, new VoteRequest(VoterA, 1, 0, 0));
    internal ReplicaPeerEnvelope Read() => Request(ReplicaRpc.ReadBarrier, string.Empty);
    internal ReplicaPeerEnvelope Forward(OperationKind kind) => Request(ReplicaRpc.Forward, Operation(kind));
    internal ReplicaPeerEnvelope Append(params OperationKind?[] kinds) => Request(ReplicaRpc.Append,
        new AppendRequest(VoterA, 1, 0, 0, 0, kinds.Select((kind, index) => new ReplicaEntry(index + 1L, 1,
            kind is { } value ? Operation(value) : null)).ToImmutableArray()));
    internal ReplicaPeerEnvelope Request<T>(ReplicaRpc method, T payload, string? sender = null)
    {
        var request = Sender.SignRequest(VoterB, method, Discovery.RuntimeAddress, ReplicaProtocolCodec.Serialize(payload));
        return sender is null ? request : Resign(request with { Sender = sender });
    }
    internal ReplicaPeerEnvelope Resign(ReplicaPeerEnvelope request) => request with { Signature = mac.Request(request) };
    internal ReplicaPeerEnvelope InvalidControl(string scenario)
    {
        var request = Forward(OperationKind.Membership);
        var source = ReplicaTransportProtocol.Utf8.GetString(request.Payload.Span);
        var payload = scenario switch
        {
            DuplicateKind => source.Replace(KindToken, DuplicateKind.TrimStart('{').TrimEnd('}'), StringComparison.Ordinal),
            UnknownKind => source.Replace(KindToken, UnknownKind.TrimStart('{').TrimEnd('}'), StringComparison.Ordinal),
            InvalidPayload => source.Replace(PayloadToken, InvalidPayloadToken, StringComparison.Ordinal),
            UnknownField => source.Replace(KindToken, UnknownFieldToken, StringComparison.Ordinal),
            _ => MissingOperationFields
        };
        return Resign(request with { Payload = ReplicaTransportProtocol.Utf8.GetBytes(payload) });
    }
    private static ReplicatedOperation Operation(OperationKind kind) => new(Guid.NewGuid(), kind, VoterA, TimeProvider.System.GetUtcNow(), OperationPayload);
    /// <inheritdoc />
    public void Dispose() { Sender.Dispose(); Receiver.Dispose(); mac.Dispose(); host.Dispose(); CryptographicOperations.ZeroMemory(credential); }
}
