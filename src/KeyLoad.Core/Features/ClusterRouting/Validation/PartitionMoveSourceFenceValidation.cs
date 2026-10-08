using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PartitionMoveSourceFenceValidation
{
    private const int HexCharactersPerByte = 2;

    internal static void Require(PartitionMoveSourceFenceRecord record, PartitionRef partition)
    {
        if (record is null || record.Version != PartitionMoveProtocol.Version || record.MoveId == Guid.Empty
            || record.Partition != partition || record.SourcePlacement is null || record.ControlOwner is null
            || record.DestinationOwner is null || record.SourcePlacement.Partition != partition
            || record.SourceCut <= PartitionMoveProtocol.EmptyCount
            || record.ControlOwner.Incarnation == Guid.Empty || record.SourcePlacement.Incarnation == Guid.Empty
            || record.DestinationOwner.Incarnation == Guid.Empty
            || record.SourcePlacement.Incarnation == record.DestinationOwner.Incarnation
            || !ValidDigest(record.ControlIntentDigest))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        DatabaseEngine.ValidatePartition(partition);
    }

    internal static bool ValidDigest(string? digest)
        => digest is not null && digest.Length == SHA256.HashSizeInBytes * HexCharactersPerByte
            && digest.All(char.IsAsciiHexDigitLower);
}
