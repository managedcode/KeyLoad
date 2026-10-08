using System.Net;
using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Fixture-private original proposals use the production MAC receiver and actual request grains.</summary>
internal sealed class ProtectedDocumentRf3SetupPeer(TwoRf3MembershipWave wave,
    PhysicalOwnerDirectoryV1 directory, string callerAddress)
{
    internal ProtectedDocumentRf3Timing Timing { get; } = new(wave);

    private const int MaximumBytes = 4 * 1024 * 1024;
    private readonly Dictionary<bool, (PartitionMovementTransportRequest Proposal, byte[] Outcome)> retained = [];
    internal PhysicalShardRecord Source => directory.ControlOwner;
    internal PhysicalShardRecord Target => directory.Owners.Single(owner =>
        owner.Owner.PhysicalShardId != Source.PhysicalShardId).Owner;

    internal async Task<T> SendAsync<T>(PartitionMovementTransportRequest proposal, bool target,
        CancellationToken token)
    {
        var node = target ? TwoRf3MembershipProtocol.Node4 : TwoRf3MembershipProtocol.Node1;
        var receiver = target ? Target : Source;
        using var http = McpCallerHttp.Create(wave.Application, node);
        var body = NativeSerialization.Serialize(proposal);
        if (body.Length > MaximumBytes)
        { throw new InvalidDataException(PartitionMovementProtocol.InvalidProof); }
        using var request = new HttpRequestMessage(HttpMethod.Post, PartitionMovementProtocol.Path);
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new(PartitionMovementProtocol.ContentType);
        var sourceKey = Convert.FromBase64String(wave.Profile.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(sourceKey, MaximumBytes);
            request.Headers.Add(PartitionMovementProtocol.SignatureHeader, mac.Sign(body, reply: false));
        }
        finally { CryptographicOperations.ZeroMemory(sourceKey); }
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        if (response.Content.Headers.ContentType?.ToString() != PartitionMovementProtocol.ContentType
            || response.Content.Headers.ContentLength is not > 0 or > MaximumBytes)
        { throw new InvalidDataException(PartitionMovementProtocol.InvalidProof); }
        var bytes = new byte[checked((int)response.Content.Headers.ContentLength.Value)];
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        var signatures = response.Headers.GetValues(PartitionMovementProtocol.SignatureHeader).Take(2).ToArray();
        var replyKey = Convert.FromBase64String(target ? wave.MovementTargetPeerKey
            ?? throw new InvalidOperationException(PartitionMovementProtocol.InvalidProof) : wave.Profile.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(replyKey, MaximumBytes);
            if (signatures.Length != 1 || !mac.Verify(bytes, signatures[0], reply: true))
            { throw new InvalidDataException(PartitionMovementProtocol.InvalidProof); }
        }
        finally { CryptographicOperations.ZeroMemory(replyKey); }
        var reply = NativeSerialization.Deserialize<PartitionMovementTransportReply>(bytes);
        if (reply.CommandId != proposal.CommandId || reply.Nonce != proposal.Envelope.Nonce
            || !PhysicalOwnerEntryValidation.SameOwner(reply.Receiver, receiver)
            || reply.Discovery.VoterId != receiver.VoterIds[0]
            || reply.Discovery.Incarnation != receiver.Incarnation || !reply.Discovery.TransportReady)
        { throw new InvalidDataException(PartitionMovementProtocol.InvalidProof); }
        if (reply.Reply.Error is { } code)
        { throw Errors.Fail(code, reply.Reply.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        var value = (T)GrainNativePayload.Read<GrainValue>(reply.Reply.Payload).Value!;
        if (proposal.Action == PartitionMovementTransportAction.Apply && proposal.Envelope.Grant is not null
            && value is PartitionMovePhaseResult phase && !retained.ContainsKey(target))
        { retained.Add(target, (proposal, JsonDefaults.Serialize(phase))); }
        return value;
    }

    internal async Task VerifyRetainedOutcomesAsync(CancellationToken token)
    {
        await Assert.That(retained.Count).IsEqualTo(2);
        foreach (var entry in retained)
        { await VerifyRetainedOutcomeAsync(entry.Key, entry.Value, token).ConfigureAwait(false); }
    }

    private async Task VerifyRetainedOutcomeAsync(bool target,
        (PartitionMovementTransportRequest Proposal, byte[] Outcome) original, CancellationToken token)
    {
        var receiver = target ? Target : Source;
        var query = new PartitionMovementOutcomeTransportRequest(original.Proposal,
            Timing.OutcomeExpiry, Guid.NewGuid(),
            MaximumBytes, 256, MaximumBytes);
        var body = NativeSerialization.Serialize(query);
        if (body.Length > MaximumBytes)
        { throw new InvalidDataException(PartitionMovementProtocol.InvalidProof); }
        using var http = McpCallerHttp.Create(wave.Application,
            target ? TwoRf3MembershipProtocol.Node4 : TwoRf3MembershipProtocol.Node1);
        using var request = new HttpRequestMessage(HttpMethod.Post, PartitionMovementProtocol.OutcomePath);
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new(PartitionMovementProtocol.ContentType);
        var key = Convert.FromBase64String(wave.Profile.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(key, MaximumBytes);
            request.Headers.Add(PartitionMovementProtocol.SignatureHeader, mac.SignOutcome(body, reply: false));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        if (response.Content.Headers.ContentType?.ToString() != PartitionMovementProtocol.ContentType
            || response.Content.Headers.ContentLength is not > 0 or > MaximumBytes)
        { throw new InvalidDataException(PartitionMovementProtocol.InvalidProof); }
        var bytes = new byte[checked((int)response.Content.Headers.ContentLength.Value)];
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        var signatures = response.Headers.GetValues(PartitionMovementProtocol.SignatureHeader).Take(2).ToArray();
        var replyKey = Convert.FromBase64String(target ? wave.MovementTargetPeerKey
            ?? throw new InvalidOperationException(PartitionMovementProtocol.InvalidProof) : wave.Profile.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(replyKey, MaximumBytes);
            if (signatures.Length != 1 || !mac.VerifyOutcome(bytes, signatures[0], reply: true))
            { throw new InvalidDataException(PartitionMovementProtocol.InvalidProof); }
        }
        finally { CryptographicOperations.ZeroMemory(replyKey); }
        var reply = NativeSerialization.Deserialize<PartitionMovementTransportReply>(bytes);
        if (reply.CommandId != original.Proposal.CommandId || reply.Nonce != query.Nonce
            || !PhysicalOwnerEntryValidation.SameOwner(reply.Receiver, receiver)
            || reply.Discovery.VoterId != receiver.VoterIds[0]
            || reply.Discovery.Incarnation != receiver.Incarnation || !reply.Discovery.TransportReady)
        { throw new InvalidDataException(PartitionMovementProtocol.InvalidProof); }
        if (reply.Reply.Error is { } code)
        { throw Errors.Fail(code, reply.Reply.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        var witness = (PartitionMovementOutcomeWitness)GrainNativePayload.Read<GrainValue>(reply.Reply.Payload).Value!;
        await Assert.That(witness.Result.Error).IsNull();
        await Assert.That(witness.Result.SafeDetail).IsNull();
        await Assert.That(JsonDefaults.Serialize(witness.Result.Get<PartitionMovePhaseResult>())
            .SequenceEqual(original.Outcome)).IsTrue();
        await Assert.That(witness.ReadBytes > 0 && witness.ReadBytes <= query.MaximumReadBytes).IsTrue();
        await Assert.That(witness.ExaminedRecords > 0 && witness.ExaminedRecords <= query.MaximumExaminedRecords).IsTrue();
    }

    internal PartitionMovementTransportRequest Proposal(Guid id, PartitionMovePeerEnvelope envelope,
        PartitionMoveJournalReceipt? authorization = null, PartitionMovementTransportAction action = PartitionMovementTransportAction.Apply,
        Guid handle = default, int ordinal = 0)
        => new(id, envelope, authorization, Source.VoterIds[0], callerAddress, action, handle, ordinal);
}
