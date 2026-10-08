using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns the configured HTTP connection, authenticated reply validation and native address pins.</summary>
internal sealed partial class PartitionMovementTransportOwner : IDisposable
{
    private const int FirstVoter = 0;
    private const int SingleSignature = 1;
    private const int MinimumBodyBytes = 1;
    private readonly NodeOptions options;
    private readonly PartitionHost partition;
    private readonly IOptions<OrleansMembershipOptions> membership;
    private readonly TimeProvider clock;
    private readonly PartitionMovementTransportResources resources;

    internal PartitionMovementTransportOwner(IOptions<NodeOptions> options, PartitionHost partition,
        IOptions<OrleansMembershipOptions> membership, TimeProvider clock)
    {
        this.options = options.Value;
        this.partition = partition;
        this.membership = membership;
        this.clock = clock;
        resources = new(options, partition, membership);
    }

    public void Dispose() => resources.Dispose();

    private const string OriginalFailureKey = "KeyLoad.PartitionMovement.OriginalTransportFailure";

    internal async Task<PartitionMovementTransportReply> ExchangeAsync(int index, RegisteredPhysicalOwnerV1 receiver,
        PartitionMovementTransportRequest original, byte[] body, bool local, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        PartitionMovementTransportReply? terminal = null;
        var submitted = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var request = CreateRequest(receiver.Endpoints[index], body);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                submitted = true;
                using var response = await resources.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    terminal = await ReadReplyAsync(response, local, cancellationToken).ConfigureAwait(false);
                    await VerifyReplyAsync(index, receiver, original, terminal, local, cancellationToken).ConfigureAwait(false);
                }, failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        try
        { ServerFailureObserver.ThrowIfAny(failures); }
        catch (Exception error) when (submitted && IsUnresolvedTransport(error))
        {
            var unknown = Errors.Fail(ErrorCode.UnknownWriteOutcome, PartitionMovementProtocol.Unavailable);
            unknown.Data[OriginalFailureKey] = error;
            throw unknown;
        }
        return terminal ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
    }

    private static bool IsUnresolvedTransport(Exception error)
        => error is HttpRequestException or IOException or TimeoutException
            || error is AggregateException aggregate && aggregate.Flatten().InnerExceptions.All(IsUnresolvedTransport);

    private HttpRequestMessage CreateRequest(string endpoint, byte[] body)
    {
        var key = Convert.FromBase64String(options.PeerSecret);
        string signature;
        try
        {
            using var mac = new PartitionMovementMac(key, partition.Database.Limits.MaxBatchBytes);
            signature = mac.Sign(body, reply: false);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(endpoint), PartitionMovementProtocol.Path));
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
    private const int NoEncodings = 0;
    private Task<PartitionMovementTransportReply> ReadReplyAsync(HttpResponseMessage response, bool local,
        CancellationToken cancellationToken)
        => ReadReplyPurposeAsync(response, local, outcome: false, cancellationToken);

    private async Task<PartitionMovementTransportReply> ReadReplyPurposeAsync(HttpResponseMessage response, bool local,
        bool outcome, CancellationToken cancellationToken)
    {
        if (response.StatusCode != System.Net.HttpStatusCode.OK
            || response.Content.Headers.ContentType?.ToString() != PartitionMovementProtocol.ContentType
            || response.Content.Headers.ContentLength is not >= MinimumBodyBytes
            || response.Content.Headers.ContentLength > partition.Database.Limits.MaxBatchBytes
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
        var key = Convert.FromBase64String((local ? options.PeerSecret : options.MembershipAuthority.TrustedGroupPeerSecret)
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof));
        try
        {
            using var mac = new PartitionMovementMac(key, partition.Database.Limits.MaxBatchBytes);
            if (signatures.Length != SingleSignature || !(outcome ? mac.VerifyOutcome(body, signatures[FirstVoter], reply: true)
                : mac.Verify(body, signatures[FirstVoter], reply: true)))
            { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        return NativeSerialization.Deserialize<PartitionMovementTransportReply>(body);
    }

    private async Task VerifyReplyAsync(int index, RegisteredPhysicalOwnerV1 receiver,
        PartitionMovementTransportRequest request, PartitionMovementTransportReply reply, bool local,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var discovery = reply.Discovery;
        if (request.Envelope.ExpiresAt <= clock.GetUtcNow() || reply.CommandId != request.CommandId
            || reply.Nonce != request.Envelope.Nonce || reply.Reply is null
            || !PhysicalOwnerEntryValidation.SameOwner(reply.Receiver, receiver.Owner)
            || discovery is not { TransportReady: true }
            || discovery.VoterId != receiver.Owner.VoterIds[index]
            || discovery.ClusterId != options.ClusterId || discovery.Incarnation != receiver.Owner.Incarnation
            || discovery.ApplicationRpcVersion != GrainRoutingProtocol.RequestInterfaceVersion
            || discovery.PeerEnvelopeVersion != ReplicaTransportProtocol.Version
            || discovery.RuntimeJournalReaderContract != StoreReaderContract.RuntimeJournal
            || !ReplicaMembershipAuthorityValidation.CanonicalAddress(discovery.SiloAddress, membership))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        PartitionMovementTransportReplyValidation.RequireValue(reply.Reply);
        var address = SiloAddress.FromParsableString(discovery.SiloAddress);
        if (address.Endpoint.Port != MembershipAuthoritySettingsProtocol.NativeSiloPort)
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        await (local ? resources.ControlPins : resources.DestinationPins).PinCallerAsync(index, address.Endpoint.Address,
            cancellationToken).ConfigureAwait(false);
    }
}
