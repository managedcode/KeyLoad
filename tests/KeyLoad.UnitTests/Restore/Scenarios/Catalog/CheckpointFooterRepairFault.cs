using System.Buffers.Binary;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.StorageRecovery;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CheckpointFooterRepairFault
{
    private const string MissingFooter = "The genuine checkpoint fixture has no complete original footer.";

    internal static byte[] Create(byte[] original)
    {
        var offset = 0;
        while (original.Length - offset >= ZoneTreePersistenceFormat.HeaderLength)
        {
            var header = original.AsSpan(offset, ZoneTreePersistenceFormat.HeaderLength);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header[ZoneTreePersistenceFormat.PayloadLengthOffset..]);
            if (length < 0 || length > original.Length - offset - ZoneTreePersistenceFormat.HeaderLength)
            {
                throw new InvalidOperationException(MissingFooter);
            }
            var end = checked(offset + ZoneTreePersistenceFormat.HeaderLength + length);
            if (BinaryPrimitives.ReadUInt64LittleEndian(header) == ZoneTreePersistenceFormat.CheckpointEndMagic)
            {
                var footer = OrleansSerialization.Deserialize<ZoneTreeCheckpointFooter>(original.AsSpan(
                    offset + ZoneTreePersistenceFormat.HeaderLength, length));
                var sequence = BinaryPrimitives.ReadInt64LittleEndian(header[ZoneTreePersistenceFormat.SequenceOffset..]);
                var replacement = WalFileFixture.CreateFrame(OrleansSerialization.Serialize(
                    footer with
                    {
                        Records = checked(footer.Records + 1L)
                    }), sequence,
                    ZoneTreePersistenceFormat.CheckpointEndMagic);
                return [.. original[..offset], .. replacement, .. original[end..]];
            }
            offset = end;
        }
        throw new InvalidOperationException(MissingFooter);
    }
}
