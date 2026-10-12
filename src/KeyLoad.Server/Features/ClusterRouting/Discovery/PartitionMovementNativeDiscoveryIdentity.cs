using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Authenticates exact native discovery bytes using the original native discovery MAC.</summary>
internal static class PartitionMovementNativeDiscoveryIdentity
{
    internal static ReplicaSiloDiscovery ReadOwned(PartitionMovementGrainServiceDiscovery reply)
    {
        if (reply is null || reply.Payload.IsEmpty || reply.Payload.Length > ReplicaTransportProtocol.MaximumDiscoveryBytes
            || string.IsNullOrEmpty(reply.Signature) || reply.Signature.Length != ReplicaTransportProtocol.HashHexCharacters
            || !reply.Signature.All(Uri.IsHexDigit))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        return NativeSerialization.Deserialize<ReplicaSiloDiscovery>(reply.Payload.Span);
    }

    internal static void Require(NodeOptions options, ReplicaConfiguration configuration,
        RegisteredPhysicalOwnerV1 expected, SiloAddress address, Guid nonce,
        PartitionMovementGrainServiceDiscovery reply, ReplicaSiloDiscovery discovery,
        IOptions<OrleansMembershipOptions> membership)
    {
        var owned = reply.Payload.ToArray();
        var key = Convert.FromBase64String(Secret(options, configuration, expected));
        try
        {
            using var mac = new ReplicaMessageMac(key, options.ClusterId);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(reply.Signature),
                mac.Discovery(expected.Owner.Incarnation, discovery.VoterId, nonce, owned)))
            { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        var actual = NativeSerialization.Deserialize<ReplicaSiloDiscovery>(owned);
        if (actual != discovery || actual is not { TransportReady: true }
            || !expected.Owner.VoterIds.Contains(actual.VoterId, StringComparer.Ordinal)
            || actual.ClusterId != options.ClusterId || actual.Incarnation != expected.Owner.Incarnation
            || actual.ApplicationRpcVersion != GrainRoutingProtocol.RequestInterfaceVersion
            || actual.PeerEnvelopeVersion != ReplicaTransportProtocol.Version
            || actual.RuntimeJournalReaderContract != StoreReaderContract.RuntimeJournal
            || actual.SiloAddress != address.ToParsableString()
            || !PhysicalGroupAddress.CanonicalAddress(actual.SiloAddress, membership))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
    }

    internal static string Secret(NodeOptions options, ReplicaConfiguration configuration, RegisteredPhysicalOwnerV1 owner)
    {
        var control = PhysicalOwnerConfiguredTuples.Control(options, configuration);
        var destination = PhysicalOwnerConfiguredTuples.Destination(options, configuration);
        if (!PhysicalOwnerEntryValidation.SameOwner(owner.Owner, control.Owner)
            && !PhysicalOwnerEntryValidation.SameOwner(owner.Owner, destination.Owner))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        var local = PhysicalOwnerConfiguredTuples.Local(options, configuration);
        if (PhysicalOwnerEntryValidation.SameOwner(owner.Owner, local.Owner))
        { return options.PeerSecret; }
        return options.PhysicalGroupTrust.ForeignPeerSecret
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
    }

    internal static string ReplySecret(NodeOptions options, ReplicaConfiguration configuration, bool local)
    {
        var expected = local ? PhysicalOwnerConfiguredTuples.Local(options, configuration)
            : options.PhysicalGroupTrust.Role switch
            {
                PhysicalGroupTrustRole.Control => PhysicalOwnerConfiguredTuples.Destination(options, configuration),
                PhysicalGroupTrustRole.Destination => PhysicalOwnerConfiguredTuples.Control(options, configuration),
                _ => throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof)
            };
        return Secret(options, configuration, expected);
    }
}
