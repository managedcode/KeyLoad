using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Identity;

/// <summary>Freezes original native phase, grant and absolute expiry independently from transport read nonces.</summary>
internal static class PartitionMoveOriginalDispatchIdentity
{
    internal static string Digest(Guid commandId, PartitionMovePhaseCommand phase,
        PartitionMovePhaseGrant? grant, DateTimeOffset originalExpiry)
        => Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(
            new PartitionMoveOriginalDispatchScope(commandId, phase with { ReceiverEffectAdmission = null }, grant, originalExpiry))));

    internal static string Digest(Guid commandId, PartitionMovePeerEnvelope envelope)
    {
        var phase = new PartitionMovePhaseCommand(envelope.Version, envelope.MoveId, envelope.Partition,
            envelope.ControlOwner, envelope.SourcePlacement, envelope.DestinationOwner,
            envelope.ControlIntentDigest, envelope.Stage, envelope.PageOrdinal, envelope.Body,
            envelope.Grant?.GrantId, envelope.Grant?.Resources ?? ImmutableArray<ResourceDefinition>.Empty);
        return Digest(commandId, phase, envelope.Grant, envelope.ExpiresAt);
    }
}
