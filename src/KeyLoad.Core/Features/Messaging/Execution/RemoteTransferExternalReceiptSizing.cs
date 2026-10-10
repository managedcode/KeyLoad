using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private int RemoteTransferReceiptReservation(IKeyValueView view, RemoteTransferRemoteTarget target,
        QueueLaneRef source, Guid transferId, QueueLaneRef destination, string subject, string fingerprint)
    {
        var directory = PhysicalOwnerDirectorySerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferPeerProtocol.Unavailable);
        var sourceOwner = directory.Owners.Single(entry => entry.Owner.PhysicalShardId == directory.ControlOwner.PhysicalShardId);
        var targetCut = new CommitToken(target.DestinationOwner.Owner.Incarnation, destination.Partition.AtomicPartitionId,
            RemoteTransferPeerProtocol.MaximumPosition, RemoteTransferPeerProtocol.MaximumPosition);
        var receipt = new RemoteTransferReceiptClaims(RemoteTransferProtocol.ReceiptPurpose, targetCut.Incarnation,
            source, transferId, destination, subject, fingerprint, targetCut);
        var originalMaximum = CoreNativeClaims.MeasureCharacters(receipt);
        var sourceCut = Token(view, source.Partition, RemoteTransferPeerProtocol.MaximumPosition) with
        { OwnershipEpoch = RemoteTransferPeerProtocol.MaximumPosition };
        const char NativeAsciiSizingCharacter = 'A';
        var wrapper = new RemoteTransferExternalReceipt(RemoteTransferPeerProtocol.ExternalReceiptPurpose,
            sourceOwner, target.DestinationOwner, source, destination, transferId, subject, fingerprint,
            new string(NativeAsciiSizingCharacter, originalMaximum), targetCut, sourceCut,
            Convert.ToHexString(new byte[SHA256.HashSizeInBytes]));
        var maximum = CoreNativeClaims.MeasureCharacters(wrapper);
        RequireNativeBudget(maximum);
        return maximum;
    }
}
