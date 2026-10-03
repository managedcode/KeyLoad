using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.Features.InternalSerialization;

// Normalize only the official header lookup's proven unknown well-known metadata.
internal static class NativeFieldHeaderReader
{
    internal static Field Read<TInput>(ref Reader<TInput> reader)
    {
        var saved = reader;
        try
        {
            return reader.ReadFieldHeader();
        }
        catch (KeyNotFoundException)
        {
            if (!HasUnknownWellKnownType(saved))
            {
                throw;
            }
            throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
        }
    }

    private static bool HasUnknownWellKnownType<TInput>(Reader<TInput> saved)
    {
        // Copied stream/buffer inputs can share mutable I/O. Only span cursor copies are probed.
        if (typeof(TInput) != typeof(SpanReaderInput))
        {
            return false;
        }
        var tag = new Tag(saved.ReadByte());
        if (!tag.IsSchemaTypeValid || tag.SchemaType != SchemaType.WellKnown)
        {
            return false;
        }
        if (!tag.IsFieldIdValid)
        {
            _ = saved.ReadVarUInt32();
        }
        var typeId = saved.ReadVarUInt32();
        return !saved.Session.WellKnownTypes.TryGetWellKnownType(typeId, out _);
    }
}
