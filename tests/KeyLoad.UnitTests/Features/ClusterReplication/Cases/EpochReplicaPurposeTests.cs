using System.Security.Cryptography;
using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class EpochReplicaPurposeTests
{
    private const string UnsupportedRequestPurposeOne = "keyload-replica-request-unsupported-one";
    private const string UnsupportedReplyPurposeOne = "keyload-replica-reply-unsupported-one";
    private const string UnsupportedDiscoveryPurposeOne = "keyload-replica-discovery-unsupported-one";
    private const string CurrentRequestPurpose = "keyload-replica-request-data-epoch7-rpc3";
    private const string CurrentReplyPurpose = "keyload-replica-reply-data-epoch7-rpc3";
    private const string CurrentDiscoveryPurpose = "keyload-replica-discovery-data-epoch7";
    private const string UnsupportedRequestPurposeTwo = "keyload-replica-request-unsupported-two";
    private const string UnsupportedReplyPurposeTwo = "keyload-replica-reply-unsupported-two";
    private const string UnsupportedDiscoveryPurposeTwo = "keyload-replica-discovery-unsupported-two";
    private const int UnsupportedPeerVersion = int.MaxValue;
    private const int SnapshotTerm = 1;
    private const int SnapshotPosition = 1;
    private const int SnapshotLength = 1;
    private const byte SnapshotContent = 1;

    [Test]
    [Arguments(ReplicaRpc.RequestVote, UnsupportedRequestPurposeOne)]
    [Arguments(ReplicaRpc.RequestVote, UnsupportedRequestPurposeTwo)]
    [Arguments(ReplicaRpc.Append, UnsupportedRequestPurposeOne)]
    [Arguments(ReplicaRpc.Append, UnsupportedRequestPurposeTwo)]
    [Arguments(ReplicaRpc.SnapshotBegin, UnsupportedRequestPurposeOne)]
    [Arguments(ReplicaRpc.SnapshotBegin, UnsupportedRequestPurposeTwo)]
    [Arguments(ReplicaRpc.SnapshotChunk, UnsupportedRequestPurposeOne)]
    [Arguments(ReplicaRpc.SnapshotChunk, UnsupportedRequestPurposeTwo)]
    [Arguments(ReplicaRpc.SnapshotComplete, UnsupportedRequestPurposeOne)]
    [Arguments(ReplicaRpc.SnapshotComplete, UnsupportedRequestPurposeTwo)]
    public async Task AcEpoch005UnsupportedPurposeRequestsFailBeforePayloadAdmission(ReplicaRpc method, string rejectedPurpose)
    {
        using var fixture = new ReplicaSecurityFixture();
        var request = CurrentRequest(fixture, method);
        var unsupported = request with { Signature = EpochReplicaMac.Request(fixture.Options, request, rejectedPurpose) };
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(unsupported));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Unauthenticated);

        using var currentMac = new ReplicaMessageMac(fixture.Options.Secret, fixture.Options.ClusterId);
        var accepted = unsupported with { Signature = currentMac.Request(unsupported) };
        fixture.Receiver.VerifyRequest(accepted);
        await AssertCurrentReplicaContractAsync(accepted, fixture);
    }

    [Test]
    [Arguments(UnsupportedReplyPurposeOne)]
    [Arguments(UnsupportedReplyPurposeTwo)]
    public async Task AcEpoch005UnsupportedPurposeRepliesCannotPassRequestBoundVerification(string rejectedPurpose)
    {
        using var fixture = new ReplicaSecurityFixture();
        var request = fixture.Vote();
        var current = fixture.Receiver.CreateReply(request, ReadOnlyMemory<byte>.Empty);
        var unsupported = current with { Signature = EpochReplicaMac.Reply(fixture.Options, current, rejectedPurpose) };
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Sender.VerifyReply(request, unsupported));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Unauthenticated);
        fixture.Sender.VerifyReply(request, current);
        await Assert.That(ReplicaTransportProtocol.ReplyPurpose).IsEqualTo(CurrentReplyPurpose);
    }

    [Test]
    public async Task AcCrs003UnsupportedPeerEnvelopeCannotAcknowledgeCurrentRequests()
    {
        using var fixture = new ReplicaSecurityFixture();
        var currentRequest = fixture.Vote();
        var unsupportedRequest = currentRequest with { Version = UnsupportedPeerVersion };
        unsupportedRequest = unsupportedRequest with
        {
            Signature = EpochReplicaMac.Request(fixture.Options, unsupportedRequest,
                UnsupportedRequestPurposeOne, UnsupportedPeerVersion)
        };
        var rejectedRequest = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(unsupportedRequest));
        await Assert.That(rejectedRequest.Code).IsEqualTo(ErrorCode.Unauthenticated);

        var currentReply = fixture.Receiver.CreateReply(currentRequest, ReadOnlyMemory<byte>.Empty);
        var unsupportedReply = currentReply with { Version = UnsupportedPeerVersion };
        unsupportedReply = unsupportedReply with
        {
            Signature = EpochReplicaMac.Reply(fixture.Options, unsupportedReply,
                UnsupportedReplyPurposeOne, UnsupportedPeerVersion)
        };
        var rejectedReply = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Sender.VerifyReply(currentRequest, unsupportedReply));
        await Assert.That(rejectedReply.Code).IsEqualTo(ErrorCode.Unauthenticated);
        fixture.Sender.VerifyReply(currentRequest, currentReply);
    }

    [Test]
    [Arguments(UnsupportedDiscoveryPurposeOne)]
    [Arguments(UnsupportedDiscoveryPurposeTwo)]
    public async Task AcEpoch005UnsupportedPurposeDiscoveryResponsesFailBeforeAddressAcceptance(string rejectedPurpose)
    {
        using var fixture = new ReplicaSecurityFixture();
        var nonce = Guid.NewGuid();
        var bytes = NativeSerialization.Serialize(fixture.Discovery.Read());
        var unsupported = EpochReplicaMac.Discovery(fixture.Options, fixture.Configuration.Incarnation,
            ReplicaSecurityFixture.VoterB, nonce, bytes, rejectedPurpose);
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Sender.VerifyDiscovery(
            ReplicaSecurityFixture.VoterB, bytes, nonce, unsupported));
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
