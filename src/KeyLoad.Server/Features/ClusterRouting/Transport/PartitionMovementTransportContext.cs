using System.Security.Cryptography;
using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Borrows the actual silo client; the original runtime owns admission and joined shutdown.</summary>
internal sealed class PartitionMovementTransportContext : IDisposable
{
    internal const string OriginalFailureKey = "KeyLoad.PartitionMovement.OriginalTransportFailure";
    private const int Open = 0;
    private const int Closed = 1;
    private readonly OrleansNode node;
    private int closed;
    internal PartitionHost Partition
    {
        get;
    }
    internal IOptions<OrleansMembershipOptions> Membership
    {
        get;
    }
    internal TimeProvider Clock
    {
        get;
    }
    internal NodeOptions Options
    {
        get;
    }

    internal PartitionMovementTransportContext(IOptions<NodeOptions> options, OrleansNode node,
        PartitionHost partition, IOptions<OrleansMembershipOptions> membership, TimeProvider clock)
    {
        Options = options.Value;
        this.node = node;
        Partition = partition;
        Membership = membership;
        Clock = clock;
    }

    private PartitionMovementGrainServiceClient Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref closed) != Open, this);
            return node.BorrowPartitionMovementTransport();
        }
    }

    internal async Task<SiloAddress> ResolveAsync(RegisteredPhysicalOwnerV1 receiver, int index,
        bool readOnly, CancellationToken token)
    {
        try
        {
            return await Native.ResolveAsync(receiver, index, token).ConfigureAwait(false);
        }
        catch (PartitionMovementNativeRouteUnavailableException failure) when (readOnly)
        {
            throw new PartitionMovementOutcomeNetworkException(failure);
        }
    }

    internal async Task<PartitionMovementGrainServiceReply> ExchangeAsync(SiloAddress address,
        string voter, PartitionMovementGrainServiceChannel channel, bool query,
        ReadOnlyMemory<byte> body, string signature, bool readOnly, CancellationToken token)
    {
        var client = Native;
        var request = client.CreateRequest(channel, query, body, signature, voter, address);
        try
        {
            return await client.ExchangeAsync(address, request, token).ConfigureAwait(false);
        }
        catch (Exception failure) when (readOnly && (failure is OrleansException or TimeoutException))
        {
            throw new PartitionMovementOutcomeNetworkException(failure);
        }
    }

    internal string Sign(byte[] body, PartitionMovementTransportReplyVerifier.ReceiverReplyPurpose purpose)
    {
        var key = Convert.FromBase64String(Options.PeerSecret);
        try
        {
            using var mac = new PartitionMovementMac(key, Partition.Database.Limits.MaxBatchBytes);
            return purpose == PartitionMovementTransportReplyVerifier.ReceiverReplyPurpose.Outcome
                ? mac.SignOutcome(body, reply: false) : mac.Sign(body, reply: false);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public void Dispose() => Volatile.Write(ref closed, Closed);

    internal static bool IsUnresolvedTransport(Exception error)
        => error is OrleansException or IOException or TimeoutException
            || error is AggregateException aggregate && aggregate.Flatten().InnerExceptions.All(IsUnresolvedTransport);
}
