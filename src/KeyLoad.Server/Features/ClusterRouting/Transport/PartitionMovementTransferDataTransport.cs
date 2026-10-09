using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Storage;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns transfer transport responsibility while borrowing the original HTTP, native address pins and configured authority.</summary>
internal sealed class PartitionMovementTransferDataTransport(PartitionMovementTransportContext context)
{
    private const int FirstVoter = 0;
    private const int SingleSignature = 1;
    private const int MinimumBodyBytes = 1;
    private const int NoEncodings = 0;
    internal async Task<PartitionMovementTransferDataReply> ExchangeTransferDataAsync(int index,
        RegisteredPhysicalOwnerV1 source, PartitionMovementTransferDataRequest original, byte[] body,
        bool local, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        PartitionMovementTransferDataReply? terminal = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var request = CreateTransferDataRequest(source.Endpoints[index], body);
            await ServerFailureObserver.ObserveAsync(() => SendTransferDataAsync(request, source, index, original,
                local, value => terminal = value, cancellationToken), failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return terminal ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
    }

    internal async Task SendTransferDataAsync(HttpRequestMessage request, RegisteredPhysicalOwnerV1 source, int index,
        PartitionMovementTransferDataRequest original, bool local, Action<PartitionMovementTransferDataReply> receive,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var response = await context.Resources.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var reply = await ReadTransferDataReplyAsync(response, local, cancellationToken).ConfigureAwait(false);
                await VerifyTransferDataReplyAsync(index, source, original, reply, local, cancellationToken).ConfigureAwait(false);
                receive(reply);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal HttpRequestMessage CreateTransferDataRequest(string endpoint, byte[] body)
    {
        var key = Convert.FromBase64String(context.Options.PeerSecret);
        string signature;
        try
        {
            using var mac = new PartitionMovementMac(key, context.Partition.Database.Limits.MaxBatchBytes);
            signature = mac.SignTransferData(body, reply: false);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(endpoint), PartitionMovementProtocol.TransferDataPath));
        try
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new(PartitionMovementProtocol.ContentType);
            request.Headers.Add(PartitionMovementProtocol.SignatureHeader, signature);
            return request;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(request.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal async Task<PartitionMovementTransferDataReply> ReadTransferDataReplyAsync(HttpResponseMessage response,
        bool local, CancellationToken cancellationToken)
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
        if (!response.Headers.TryGetValues(PartitionMovementProtocol.SignatureHeader, out var values))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var signatures = values.ToArray();
        if (signatures.Length != SingleSignature)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var key = Convert.FromBase64String(local ? context.Options.PeerSecret : context.Options.MembershipAuthority.TrustedGroupPeerSecret
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof));
        try
        {
            using var mac = new PartitionMovementMac(key, context.Partition.Database.Limits.MaxBatchBytes);
            if (!mac.VerifyTransferData(body, signatures[FirstVoter], reply: true))
            { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        return NativeSerialization.Deserialize<PartitionMovementTransferDataReply>(body);
    }

    internal async Task VerifyTransferDataReplyAsync(int index, RegisteredPhysicalOwnerV1 source,
        PartitionMovementTransferDataRequest original, PartitionMovementTransferDataReply reply,
        bool local, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var discovery = reply.Discovery;
        if (original.ExpiresAt <= context.Clock.GetUtcNow() || reply.RequestId != original.RequestId
            || reply.Nonce != original.Nonce || reply.Reply is null
            || !PhysicalOwnerEntryValidation.SameOwner(reply.SourceOwner, source.Owner)
            || discovery is not { TransportReady: true }
            || discovery.VoterId != source.Owner.VoterIds[index] || discovery.ClusterId != context.Options.ClusterId
            || discovery.Incarnation != source.Owner.Incarnation
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
