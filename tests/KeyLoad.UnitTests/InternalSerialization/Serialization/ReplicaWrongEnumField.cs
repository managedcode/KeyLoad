using System.Buffers;
using Orleans.Serialization.Buffers;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

// Actual native producers preserve every field except the one deliberately wrong scalar metadata type.
internal static class ReplicaWrongEnumField
{
    private const string WrongValue = "invalid-enum-value";

    internal static void Write<TBufferWriter>(ref Writer<TBufferWriter> writer, Type expectedType,
        int value, ReplicaWrongEnumScalar scalar) where TBufferWriter : IBufferWriter<byte>
    {
        if (scalar == ReplicaWrongEnumScalar.Int64)
        {
            writer.Session.CodecProvider.GetCodec<long>().WriteField(ref writer, 1, expectedType, value);
        }
        else
        {
            writer.Session.CodecProvider.GetCodec<string>().WriteField(ref writer, 1, expectedType, WrongValue);
        }
    }
}
