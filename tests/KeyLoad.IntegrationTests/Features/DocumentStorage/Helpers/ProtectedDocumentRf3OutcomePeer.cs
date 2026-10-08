using System.Net;
using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Calls only the actual configured signed native outcome receiver through Aspire endpoints.</summary>
internal sealed class ProtectedDocumentRf3OutcomePeer(TwoRf3MembershipWave wave, ProtectedDocumentRf3SetupPeer peer)
{
    private const int MaximumBytes = 4 * 1024 * 1024;

    internal async Task<PartitionMovementOutcomeWitness?> QueryAsync(PartitionMovementTransportRequest original,
        bool target, CancellationToken token, bool wrongPath = false, bool badMac = false,
        ErrorCode? expectedError = null)
    {
        var query = new PartitionMovementOutcomeTransportRequest(original,
            peer.Timing.OutcomeExpiry, Guid.NewGuid(), MaximumBytes, 256, MaximumBytes);
        var body = NativeSerialization.Serialize(query);
        if (body.Length > MaximumBytes)
        { throw new InvalidDataException(PartitionMovementProtocol.InvalidProof); }
        using var http = McpCallerHttp.Create(wave.Application,
            target ? TwoRf3MembershipProtocol.Node4 : TwoRf3MembershipProtocol.Node1);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            PartitionMovementProtocol.OutcomePath + (wrongPath ? "/wrong-purpose" : string.Empty));
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new(PartitionMovementProtocol.ContentType);
        Sign(request, body, badMac);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (wrongPath || badMac)
        {
            await Assert.That(response.StatusCode).IsEqualTo(wrongPath ? HttpStatusCode.NotFound : HttpStatusCode.Unauthorized);
            return null;
        }
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        if (response.Content.Headers.ContentType?.ToString() != PartitionMovementProtocol.ContentType
            || response.Content.Headers.ContentLength is not > 0 or > MaximumBytes)
        { throw new InvalidDataException(PartitionMovementProtocol.InvalidProof); }
        var bytes = new byte[checked((int)response.Content.Headers.ContentLength.Value)];
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        VerifyMac(response, bytes, target);
        var receiver = target ? peer.Target : peer.Source;
        var reply = NativeSerialization.Deserialize<PartitionMovementTransportReply>(bytes);
        if (reply.CommandId != original.CommandId || reply.Nonce != query.Nonce
            || !PhysicalOwnerEntryValidation.SameOwner(reply.Receiver, receiver)
            || reply.Discovery.VoterId != receiver.VoterIds[0]
            || reply.Discovery.Incarnation != receiver.Incarnation || !reply.Discovery.TransportReady)
        { throw new InvalidDataException(PartitionMovementProtocol.InvalidProof); }
        if (expectedError is { } denied)
        {
            await Assert.That(reply.Reply.Error).IsEqualTo(denied);
            await Assert.That(reply.Reply.Payload.IsEmpty).IsTrue();
            return null;
        }
        if (reply.Reply.Error is { } code)
        { throw Errors.Fail(code, reply.Reply.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        var witness = (PartitionMovementOutcomeWitness)GrainNativePayload.Read<GrainValue>(reply.Reply.Payload).Value!;
        await Assert.That(witness.Result.Error).IsNull();
        await Assert.That(witness.Result.SafeDetail).IsNull();
        await Assert.That(witness.ReadBytes > 0 && witness.ReadBytes <= query.MaximumReadBytes).IsTrue();
        await Assert.That(witness.ExaminedRecords > 0 && witness.ExaminedRecords <= query.MaximumExaminedRecords).IsTrue();
        return witness;
    }

    private void Sign(HttpRequestMessage request, byte[] body, bool badMac)
    {
        var key = Convert.FromBase64String(wave.Profile.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(key, MaximumBytes);
            var signature = mac.SignOutcome(body, reply: false);
            if (badMac)
            { signature = (signature[0] == '0' ? "1" : "0") + signature[1..]; }
            request.Headers.Add(PartitionMovementProtocol.SignatureHeader, signature);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private void VerifyMac(HttpResponseMessage response, byte[] bytes, bool target)
    {
        var signatures = response.Headers.GetValues(PartitionMovementProtocol.SignatureHeader).Take(2).ToArray();
        var key = Convert.FromBase64String(target ? wave.MovementTargetPeerKey
            ?? throw new InvalidOperationException(PartitionMovementProtocol.InvalidProof) : wave.Profile.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(key, MaximumBytes);
            if (signatures.Length != 1 || !mac.VerifyOutcome(bytes, signatures[0], reply: true))
            { throw new InvalidDataException(PartitionMovementProtocol.InvalidProof); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
