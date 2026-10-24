using System.Buffers;
using KeyLoad.Storage.ZoneTree;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;

namespace KeyLoad.UnitTests.BackupRestore;

internal static class MetadataCheckpointExtraField
{
    private const int ObjectTerminators = 2;
    private const uint NextFieldDelta = 1;
    private const uint UnexpectedValue = 1;

    internal static byte[] Encode(ZoneTreeCheckpointMetadata metadata)
    {
        var payload = OrleansSerialization.Serialize(metadata);
        using var session = OrleansSerialization.Serializer.SessionPool.GetSession();
        var buffer = new ArrayBufferWriter<byte>();
        var writer = Writer.Create(buffer, session);
        UInt32Codec.WriteField(ref writer, NextFieldDelta, UnexpectedValue);
        writer.Commit();
        // Metadata and its owning RuntimePayload each end with an object terminator.
        return [.. payload[..^ObjectTerminators], .. buffer.WrittenSpan, .. payload[^ObjectTerminators..]];
    }
}
