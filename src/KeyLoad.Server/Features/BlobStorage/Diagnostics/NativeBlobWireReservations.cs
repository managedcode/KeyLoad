using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.BlobStorage;

internal sealed class NativeBlobWireReservations(NativeBlobWireFiles files, NativeBlobWireCodec codec,
    IOptions<RequestProbeExecutionOptions> limits)
{
    private const int FirstSlot = 0;

    internal NativeBlobWireReservation Enter(NativeBlobWireOwner owner, string ownerDigest,
        NativeBlobWireClaim claim, string claimDigest)
        => files.WithPublication(() =>
    {
        for (var slot = FirstSlot; slot < limits.Value.MaximumActiveGates; slot++)
        {
            var name = NativeBlobWireNames.Slot(slot);
            if (files.Exists(name))
            {
                Require(codec.Read<NativeBlobWireReservation>(files.Read(name)), owner, ownerDigest, slot);
                continue;
            }
            var reservation = new NativeBlobWireReservation(NativeBlobWireProbeProtocol.Version,
                owner.SessionId, ownerDigest, claimDigest, claim.Identity, slot, Guid.NewGuid());
            files.WriteLocked(name, codec.Encode(reservation));
            return reservation;
        }
        throw new InvalidOperationException(NativeBlobWireProbeProtocol.Contention);
    });

    internal void Leave(NativeBlobWireReservation reservation)
        => files.WithPublication(() => files.RemoveLocked(NativeBlobWireNames.Slot(reservation.Slot), codec.Encode(reservation)));

    private void Require(NativeBlobWireReservation value, NativeBlobWireOwner owner,
        string ownerDigest, int slot)
    {
        if (value.Version != NativeBlobWireProbeProtocol.Version || value.SessionId != owner.SessionId
            || value.OwnerDigest != ownerDigest || value.Slot != slot || value.CallbackId == Guid.Empty
            || !Enum.IsDefined(value.Identity) || !NativeBlobWireShape.IsDigest(value.ClaimDigest))
        {
            throw new InvalidOperationException(NativeBlobWireProbeProtocol.InvalidEvidence);
        }
        var bytes = files.Read(NativeBlobWireNames.Claim(value.Identity));
        var claim = codec.Read<NativeBlobWireClaim>(bytes);
        if (NativeBlobWireCodec.Digest(bytes) != value.ClaimDigest || claim.Identity != value.Identity
            || claim.SessionId != owner.SessionId || claim.OwnerDigest != ownerDigest
            || claim.Version != NativeBlobWireProbeProtocol.Version)
        {
            throw new InvalidOperationException(NativeBlobWireProbeProtocol.InvalidEvidence);
        }
    }
}
