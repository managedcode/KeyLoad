using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Storage;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns reply transport responsibility while borrowing the original HTTP, native address pins and configured authority.</summary>
internal sealed class PartitionMovementTransportReplyVerifier(PartitionMovementTransportContext context)
{
    private const int FirstVoter = 0;
    private const int SingleSignature = 1;
    private const int MinimumBodyBytes = 1;
    private const int NoEncodings = 0;
    internal enum ReceiverReplyPurpose { Effect, Outcome, ReceiverIssue, RetireCancellation }
    internal Task<PartitionMovementAuthenticatedReply> ReadReplyAsync(HttpResponseMessage response, bool local,
        CancellationToken cancellationToken)
        => ReadReplyPurposeAsync(response, local, outcome: false, cancellationToken);

    internal async Task<PartitionMovementAuthenticatedReply> ReadReplyPurposeAsync(HttpResponseMessage response, bool local,
        bool outcome, CancellationToken cancellationToken)
        => await ReadReplyMacPurposeAsync(response, local, outcome ? ReceiverReplyPurpose.Outcome
            : ReceiverReplyPurpose.Effect, cancellationToken).ConfigureAwait(false);

    internal async Task<PartitionMovementAuthenticatedReply> ReadReplyMacPurposeAsync(HttpResponseMessage response,
        bool local, ReceiverReplyPurpose purpose, CancellationToken cancellationToken)
    {
        if (response.StatusCode != System.Net.HttpStatusCode.OK
            || response.Content.Headers.ContentType?.ToString() != PartitionMovementProtocol.ContentType
            || response.Content.Headers.ContentLength is not >= MinimumBodyBytes
            || response.Content.Headers.ContentLength > context.Partition.Database.Limits.MaxBatchBytes
            || response.Content.Headers.ContentEncoding.Count != NoEncodings
            || response.Headers.TransferEncoding.Count != NoEncodings)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable); }
        var body = new byte[checked((int)response.Content.Headers.ContentLength.Value)];
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(async () =>
            { await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false); }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        var signatures = response.Headers.TryGetValues(PartitionMovementProtocol.SignatureHeader, out var values)
            ? values.Take(SingleSignature + SingleSignature).ToArray() : [];
        var key = Convert.FromBase64String((local ? context.Options.PeerSecret : context.Options.MembershipAuthority.TrustedGroupPeerSecret)
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof));
        try
        {
            using var mac = new PartitionMovementMac(key, context.Partition.Database.Limits.MaxBatchBytes);
            if (signatures.Length != SingleSignature || !(purpose switch
            {
                ReceiverReplyPurpose.Effect => mac.Verify(body, signatures[FirstVoter], reply: true),
                ReceiverReplyPurpose.Outcome => mac.VerifyOutcome(body, signatures[FirstVoter], reply: true),
                ReceiverReplyPurpose.ReceiverIssue => mac.VerifyReceiverIssue(body, signatures[FirstVoter], source: false),
                ReceiverReplyPurpose.RetireCancellation => mac.VerifyRetireCancellationReply(body, signatures[FirstVoter]),
                _ => false
            }))
            { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        return new(NativeSerialization.Deserialize<PartitionMovementTransportReply>(body), body, signatures[FirstVoter]);
    }

    internal async Task VerifyReplyAsync(int index, RegisteredPhysicalOwnerV1 receiver,
        PartitionMovementTransportRequest request, PartitionMovementTransportReply reply, bool local,
        string expectedPhaseIdentityDigest, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var discovery = reply.Discovery;
        if (request.Envelope.ExpiresAt <= context.Clock.GetUtcNow() || reply.CommandId != request.CommandId
            || reply.Nonce != request.Envelope.Nonce || reply.Reply is null
            || reply.OriginalPhaseIdentityDigest != expectedPhaseIdentityDigest
            || !PhysicalOwnerEntryValidation.SameOwner(reply.Receiver, receiver.Owner)
            || discovery is not { TransportReady: true }
            || discovery.VoterId != receiver.Owner.VoterIds[index]
            || discovery.ClusterId != context.Options.ClusterId || discovery.Incarnation != receiver.Owner.Incarnation
            || discovery.ApplicationRpcVersion != GrainRoutingProtocol.RequestInterfaceVersion
            || discovery.PeerEnvelopeVersion != ReplicaTransportProtocol.Version
            || discovery.RuntimeJournalReaderContract != StoreReaderContract.RuntimeJournal
            || !ReplicaMembershipAuthorityValidation.CanonicalAddress(discovery.SiloAddress, context.Membership))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        PartitionMovementTransportReplyValidation.RequireValue(reply.Reply);
        var address = SiloAddress.FromParsableString(discovery.SiloAddress);
        if (address.Endpoint.Port != MembershipAuthoritySettingsProtocol.NativeSiloPort)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        await (local ? context.Resources.ControlPins : context.Resources.DestinationPins).PinCallerAsync(index, address.Endpoint.Address,
            cancellationToken).ConfigureAwait(false);
    }
}
