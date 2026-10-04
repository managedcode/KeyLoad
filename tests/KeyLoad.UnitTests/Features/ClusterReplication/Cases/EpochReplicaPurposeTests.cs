using System.Security.Cryptography;
using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class EpochReplicaPurposeTests
{
    private const string LegacyRequestPurpose = "keyload-replica-request-v2";
    private const string LegacyReplyPurpose = "keyload-replica-reply-v2";
    private const string LegacyDiscoveryPurpose = "keyload-replica-discovery-v2";
    private const string CurrentRequestPurpose = "keyload-replica-request-data-epoch7-rpc3";
    private const string CurrentReplyPurpose = "keyload-replica-reply-data-epoch7-rpc3";
    private const string CurrentDiscoveryPurpose = "keyload-replica-discovery-data-epoch7";
    private const string Epoch6RequestPurpose = "keyload-replica-request-data-epoch6-rpc2";
    private const string Epoch6ReplyPurpose = "keyload-replica-reply-data-epoch6-rpc2";
    private const string Epoch6DiscoveryPurpose = "keyload-replica-discovery-data-epoch6";
    private const int SnapshotTerm = 1;
    private const int SnapshotPosition = 1;
    private const int SnapshotLength = 1;
    private const byte SnapshotContent = 1;

    [Test]
    [Arguments(ReplicaRpc.RequestVote, LegacyRequestPurpose)]
    [Arguments(ReplicaRpc.RequestVote, Epoch6RequestPurpose)]
    [Arguments(ReplicaRpc.Append, LegacyRequestPurpose)]
    [Arguments(ReplicaRpc.Append, Epoch6RequestPurpose)]
    [Arguments(ReplicaRpc.SnapshotBegin, LegacyRequestPurpose)]
    [Arguments(ReplicaRpc.SnapshotBegin, Epoch6RequestPurpose)]
    [Arguments(ReplicaRpc.SnapshotChunk, LegacyRequestPurpose)]
    [Arguments(ReplicaRpc.SnapshotChunk, Epoch6RequestPurpose)]
    [Arguments(ReplicaRpc.SnapshotComplete, LegacyRequestPurpose)]
    [Arguments(ReplicaRpc.SnapshotComplete, Epoch6RequestPurpose)]
    public async Task AcEpoch005OldPurposeRequestsFailBeforePayloadAdmission(ReplicaRpc method, string rejectedPurpose)
    {
        using var fixture = new ReplicaSecurityFixture();
        var request = CurrentRequest(fixture, method);
        var stale = request with { Signature = EpochReplicaMac.Request(fixture.Options, request, rejectedPurpose) };
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(stale));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Unauthenticated);

        using var currentMac = new ReplicaMessageMac(fixture.Options.Secret, fixture.Options.ClusterId);
        var accepted = stale with { Signature = currentMac.Request(stale) };
        fixture.Receiver.VerifyRequest(accepted);
        await AssertCurrentReplicaContractAsync(accepted, fixture);
    }

    [Test]
    [Arguments(LegacyReplyPurpose)]
    [Arguments(Epoch6ReplyPurpose)]
    public async Task AcEpoch005OldPurposeRepliesCannotPassRequestBoundVerification(string rejectedPurpose)
    {
        using var fixture = new ReplicaSecurityFixture();
        var request = fixture.Vote();
        var current = fixture.Receiver.CreateReply(request, ReadOnlyMemory<byte>.Empty);
        var stale = current with { Signature = EpochReplicaMac.Reply(fixture.Options, current, rejectedPurpose) };
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Sender.VerifyReply(request, stale));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Unauthenticated);
        fixture.Sender.VerifyReply(request, current);
        await Assert.That(ReplicaTransportProtocol.ReplyPurpose).IsEqualTo(CurrentReplyPurpose);
    }

    [Test]
    public async Task AcCrs003PriorEpoch6PeerEnvelopeCannotAcknowledgeCurrentRequests()
    {
        using var fixture = new ReplicaSecurityFixture();
        var currentRequest = fixture.Vote();
        var oldRequest = currentRequest with { Version = ReplicaTransportProtocol.DiscoveryMacVersion };
        oldRequest = oldRequest with
        {
            Signature = EpochReplicaMac.Request(fixture.Options, oldRequest,
                "keyload-replica-request-data-epoch6", ReplicaTransportProtocol.DiscoveryMacVersion)
        };
        var rejectedRequest = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(oldRequest));
        await Assert.That(rejectedRequest.Code).IsEqualTo(ErrorCode.Unauthenticated);

        var currentReply = fixture.Receiver.CreateReply(currentRequest, ReadOnlyMemory<byte>.Empty);
        var oldReply = currentReply with { Version = ReplicaTransportProtocol.DiscoveryMacVersion };
        oldReply = oldReply with
        {
            Signature = EpochReplicaMac.Reply(fixture.Options, oldReply,
                "keyload-replica-reply-data-epoch6", ReplicaTransportProtocol.DiscoveryMacVersion)
        };
        var rejectedReply = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Sender.VerifyReply(currentRequest, oldReply));
        await Assert.That(rejectedReply.Code).IsEqualTo(ErrorCode.Unauthenticated);
        fixture.Sender.VerifyReply(currentRequest, currentReply);
    }

    [Test]
    [Arguments(LegacyDiscoveryPurpose)]
    [Arguments(Epoch6DiscoveryPurpose)]
    public async Task AcEpoch005OldPurposeDiscoveryResponsesFailBeforeAddressAcceptance(string rejectedPurpose)
    {
        using var fixture = new ReplicaSecurityFixture();
        var nonce = Guid.NewGuid();
        var bytes = NativeSerialization.Serialize(fixture.Discovery.Read());
        var stale = EpochReplicaMac.Discovery(fixture.Options, fixture.Configuration.Incarnation,
            ReplicaSecurityFixture.VoterB, nonce, bytes, rejectedPurpose);
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Sender.VerifyDiscovery(
            ReplicaSecurityFixture.VoterB, bytes, nonce, stale));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Unauthenticated);
        var current = fixture.Receiver.SignDiscovery(bytes, nonce);
        fixture.Sender.VerifyDiscovery(ReplicaSecurityFixture.VoterB, bytes, nonce, current);
        await Assert.That(ReplicaTransportProtocol.DiscoveryPurpose).IsEqualTo(CurrentDiscoveryPurpose);
    }

    private static ReplicaPeerEnvelope CurrentRequest(ReplicaSecurityFixture fixture, ReplicaRpc method)
    {
        var transfer = Guid.NewGuid();
        var snapshot = new ReplicaSnapshot(transfer, fixture.Configuration.Incarnation,
            SnapshotPosition, SnapshotTerm, SnapshotLength, Convert.ToHexStringLower(SHA256.HashData([SnapshotContent])),
            transfer.ToString(ReplicaTransportProtocol.NonceFormat) + ReplicaProtocol.SnapshotExtension);
        return method switch
        {
            ReplicaRpc.RequestVote => fixture.Vote(),
            ReplicaRpc.Append => fixture.Append(OperationKind.Batch),
            ReplicaRpc.SnapshotBegin => fixture.Request(method,
                new SnapshotBeginRequest(ReplicaSecurityFixture.VoterA, SnapshotTerm, snapshot)),
            ReplicaRpc.SnapshotChunk => fixture.Request(method,
                new SnapshotChunkRequest(ReplicaSecurityFixture.VoterA, SnapshotTerm, transfer, 0,
                    new byte[] { SnapshotContent })),
            _ => fixture.Request(method,
                new SnapshotCompleteRequest(ReplicaSecurityFixture.VoterA, SnapshotTerm, transfer))
        };
    }

    private static async Task AssertCurrentReplicaContractAsync(ReplicaPeerEnvelope request,
        ReplicaSecurityFixture fixture)
    {
        await Assert.That(request.Version).IsEqualTo(ReplicaTransportProtocol.Version);
        await Assert.That(ReplicaTransportProtocol.RequestPurpose).IsEqualTo(CurrentRequestPurpose);
        await Assert.That(ReplicaTransportProtocol.RequestAlias).IsEqualTo("keyload.replica.request.v1");
        await Assert.That(ReplicaTransportProtocol.ReplyAlias).IsEqualTo("keyload.replica.reply.v1");
        var opened = ReplicaLogValidation.Open(fixture.Database.Store, fixture.Configuration, fixture.Database);
        var stateBytes = fixture.Database.Store.Read(view => view.ReadOwnedValue(ReplicaProtocol.StateStorageKey));
        var persisted = ReplicaProtocolCodec.DeserializeStored<ReplicaHardState>(stateBytes!,
            fixture.Configuration.MaxAppendEntries);
        await Assert.That(persisted.Version).IsEqualTo(opened.Version);
    }
}

internal static class EpochReplicaMac
{
    private const int NoError = -1;

    internal static byte[] Request(ReplicaPeerOptions options, ReplicaPeerEnvelope request, string purpose,
        int macVersion = ReplicaTransportProtocol.Version)
        => Message(options, purpose, request.Incarnation, request.Sender, request.Recipient, request.Method,
            request.RuntimeAddress, request.Timestamp, request.Nonce, request.Payload.Span, null, null, macVersion);

    internal static byte[] Reply(ReplicaPeerOptions options, ReplicaPeerReply reply, string purpose,
        int macVersion = ReplicaTransportProtocol.Version)
        => Message(options, purpose, reply.Incarnation, reply.Sender, reply.Recipient, reply.Method,
            reply.RuntimeAddress, reply.Timestamp, reply.RequestNonce, reply.Payload.Span, reply.Error, reply.SafeDetail, macVersion);

    internal static string Discovery(ReplicaPeerOptions options, Guid incarnation, string voter, Guid nonce,
        ReadOnlySpan<byte> payload, string purpose)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, ReplicaTransportProtocol.Utf8, leaveOpen: true);
        writer.Write(purpose);
        writer.Write(ReplicaTransportProtocol.DiscoveryMacVersion);
        writer.Write(options.ClusterId);
        writer.Write(incarnation.ToByteArray());
        writer.Write(voter);
        writer.Write(nonce.ToByteArray());
        writer.Write(payload.Length);
        writer.Write(SHA256.HashData(payload));
        writer.Flush();
        return Convert.ToHexStringLower(HMACSHA256.HashData(options.Secret.Span,
            buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length))));
    }

    private static byte[] Message(ReplicaPeerOptions options, string purpose, Guid incarnation, string sender,
        string recipient, ReplicaRpc method, string address, long timestamp, Guid nonce, ReadOnlySpan<byte> payload,
        ErrorCode? error, string? detail, int macVersion)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, ReplicaTransportProtocol.Utf8, leaveOpen: true);
        writer.Write(purpose);
        writer.Write(macVersion);
        writer.Write(options.ClusterId);
        writer.Write(incarnation.ToByteArray());
        writer.Write(sender);
        writer.Write(recipient);
        writer.Write((int)method);
        writer.Write(address);
        writer.Write(timestamp);
        writer.Write(nonce.ToByteArray());
        writer.Write(error is null ? NoError : (int)error.Value);
        writer.Write(detail ?? string.Empty);
        writer.Write(payload.Length);
        writer.Write(SHA256.HashData(payload));
        writer.Flush();
        return HMACSHA256.HashData(options.Secret.Span, buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
    }
}
