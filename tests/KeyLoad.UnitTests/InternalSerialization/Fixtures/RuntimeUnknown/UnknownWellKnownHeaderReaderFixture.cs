using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal static class UnknownWellKnownHeaderReaderFixture
{
    private const string MissingScalar = "The official integer type must have a well-known ID.";
    private const string MissingFailure = "The adversarial header must fail during official type lookup.";

    internal static byte[] Header(UnknownWellKnownHeaderShape shape, uint delta)
    {
        using var session = SerializerProviders.Get(typeof(UnknownWellKnownHeaderRecord)).Sessions.GetSession();
        var writer = Writer.CreatePooled(session);
        try
        {
            switch (shape)
            {
                case UnknownWellKnownHeaderShape.Unknown:
                    UnknownWellKnownHeaderFixture.Unknown(ref writer, delta, WireType.VarInt);
                    break;
                case UnknownWellKnownHeaderShape.Known:
                    if (!session.WellKnownTypes.TryGetWellKnownTypeId(typeof(int), out var id))
                    {
                        throw new InvalidOperationException(MissingScalar);
                    }
                    UnknownWellKnownHeaderFixture.WellKnown(ref writer, delta, WireType.VarInt, id);
                    break;
                case UnknownWellKnownHeaderShape.Null:
                    UnknownWellKnownHeaderFixture.WellKnown(ref writer, delta, WireType.VarInt, 0);
                    break;
                case UnknownWellKnownHeaderShape.Expected:
                    writer.WriteFieldHeaderExpected(delta, WireType.VarInt);
                    break;
                case UnknownWellKnownHeaderShape.Encoded:
                    writer.WriteFieldHeader(delta, typeof(object), typeof(UnknownWellKnownHeaderRecord), WireType.TagDelimited);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(shape));
            }
            writer.WriteVarUInt32(UnknownWellKnownHeaderFixture.Number);
            writer.Commit();
            return writer.Output.ToArray();
        }
        finally
        {
            writer.Dispose();
        }
    }

    internal static UnknownWellKnownHeaderObservation Observe(byte[] bytes, bool guarded, bool stream)
    {
        using var session = SerializerProviders.Get(typeof(UnknownWellKnownHeaderRecord)).Sessions.GetSession();
        session.ReferencedTypes.RecordReferencedType(typeof(UnknownWellKnownHeaderRecord));
        ReferenceCodec.MarkValueField(session);
        if (stream)
        {
            using var input = new MemoryStream(bytes);
            var reader = Reader.Create(input, session);
            return Observe(ref reader, guarded);
        }
        var spanReader = Reader.Create(bytes.AsSpan(), session);
        return Observe(ref spanReader, guarded);
    }

    private static UnknownWellKnownHeaderObservation Observe<TInput>(ref Reader<TInput> reader, bool guarded)
    {
        Field? header = null;
        Exception? error = null;
        try
        {
            header = guarded ? FieldHeaderReader.Read(ref reader) : reader.ReadFieldHeader();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or KeyLoadException)
        {
            error = exception;
        }
        return new(header, error, reader.Position, reader.Remaining, reader.Session.ReferencedObjects.CurrentReferenceId,
            reader.Session.ReferencedTypes.GetReferencedType(1), reader.Session.ReferencedTypes.TryGetReferencedType(2, out _));
    }

    internal static KeyNotFoundException RequireOriginalFailure(UnknownWellKnownHeaderObservation observation)
        => observation.Error as KeyNotFoundException ?? throw new InvalidOperationException(MissingFailure);
}
