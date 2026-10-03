using System.Buffers;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.Replication;

internal static class ReplicaInspectionFields
{
    internal static string Sender<TInput>(ref Reader<TInput> reader)
        => ReplicaInspectionBuffers.For(reader.Session).Sender(Read<string, TInput>(ref reader, 0));

    internal static T Read<T, TInput>(ref Reader<TInput> reader, uint delta)
    {
        var field = reader.ReadFieldHeader();
        Require(field, delta, ExpectedFieldType<T>());
        return reader.Session.CodecProvider.GetCodec<T>().ReadValue(ref reader, field)!;
    }

    // These two current ungenerated contracts use Orleans' backing-integer codec.
    // Generated/custom enum contracts must retain their own declared metadata policy.
    private static Type ExpectedFieldType<T>()
        => typeof(T) == typeof(OperationKind) ? Enum.GetUnderlyingType(typeof(OperationKind))
            : typeof(T) == typeof(ErrorCode) || typeof(T) == typeof(ErrorCode?)
                ? Enum.GetUnderlyingType(typeof(ErrorCode)) : typeof(T);

    internal static void Require(Field field, uint delta, Type expected)
    {
        if (!field.HasFieldId || field.FieldIdDelta != delta
            || field.FieldType is not null && field.FieldType != expected)
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaPersistence.InvalidEncoding);
        }
    }

    internal static void End<TInput>(ref Reader<TInput> reader)
    {
        if (!reader.ReadFieldHeader().IsEndObject)
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaPersistence.InvalidEncoding);
        }
    }

    internal static void EndBase<TInput>(ref Reader<TInput> reader)
    {
        if (!reader.ReadFieldHeader().IsEndBaseFields)
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaPersistence.InvalidEncoding);
        }
    }

    internal static T? Reference<T, TInput>(ref Reader<TInput> reader) where T : class
    {
        ReferenceCodec.MarkValueField(reader.Session);
        var reference = reader.ReadVarUInt32();
        if (reference == 0)
        {
            return null;
        }
        return reader.Session.ReferencedObjects.TryGetReferencedObject(reference) as T
            ?? throw Errors.Fail(ErrorCode.Corruption, ReplicaPersistence.InvalidEncoding);
    }

    internal static void Write<T, TBufferWriter>(ref Writer<TBufferWriter> writer, uint delta, T value)
        where TBufferWriter : IBufferWriter<byte>
        => writer.Session.CodecProvider.GetCodec<T>().WriteField(ref writer, delta, typeof(T), value);
}
