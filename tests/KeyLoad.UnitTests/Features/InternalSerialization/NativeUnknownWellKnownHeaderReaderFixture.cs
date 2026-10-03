using KeyLoad.Features.InternalSerialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal enum NativeUnknownWellKnownHeaderShape { Unknown, Known, Null, Expected, Encoded }

internal static class NativeUnknownWellKnownHeaderReaderFixture
{
    private const string MissingScalar = "The official integer type must have a well-known ID.";
    private const string MissingFailure = "The adversarial header must fail during official type lookup.";

    internal static byte[] Header(NativeUnknownWellKnownHeaderShape shape, uint delta)
    {
        using var session = NativeSerializerProviders.Get(typeof(NativeUnknownWellKnownHeaderRecord)).Sessions.GetSession();
        var writer = Writer.CreatePooled(session);
        try
        {
            switch (shape)
            {
                case NativeUnknownWellKnownHeaderShape.Unknown:
                    NativeUnknownWellKnownHeaderFixture.Unknown(ref writer, delta, WireType.VarInt);
                    break;
                case NativeUnknownWellKnownHeaderShape.Known:
                    if (!session.WellKnownTypes.TryGetWellKnownTypeId(typeof(int), out var id))
                    {
                        throw new InvalidOperationException(MissingScalar);
                    }
                    NativeUnknownWellKnownHeaderFixture.WellKnown(ref writer, delta, WireType.VarInt, id);
                    break;
                case NativeUnknownWellKnownHeaderShape.Null:
                    NativeUnknownWellKnownHeaderFixture.WellKnown(ref writer, delta, WireType.VarInt, 0);
                    break;
                case NativeUnknownWellKnownHeaderShape.Expected:
                    writer.WriteFieldHeaderExpected(delta, WireType.VarInt);
                    break;
                case NativeUnknownWellKnownHeaderShape.Encoded:
                    writer.WriteFieldHeader(delta, typeof(object), typeof(NativeUnknownWellKnownHeaderRecord), WireType.TagDelimited);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(shape));
            }
            writer.WriteVarUInt32(NativeUnknownWellKnownHeaderFixture.Number);
            writer.Commit();
            return writer.Output.ToArray();
        }
        finally
        {
            writer.Dispose();
        }
    }

    internal static NativeUnknownWellKnownHeaderObservation Observe(byte[] bytes, bool guarded, bool stream)
    {
        using var session = NativeSerializerProviders.Get(typeof(NativeUnknownWellKnownHeaderRecord)).Sessions.GetSession();
        session.ReferencedTypes.RecordReferencedType(typeof(NativeUnknownWellKnownHeaderRecord));
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

    private static NativeUnknownWellKnownHeaderObservation Observe<TInput>(ref Reader<TInput> reader, bool guarded)
    {
        Field? header = null;
        Exception? error = null;
        try
        {
            header = guarded ? NativeFieldHeaderReader.Read(ref reader) : reader.ReadFieldHeader();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or KeyLoadException)
        {
            error = exception;
        }
        return new(header, error, reader.Position, reader.Remaining, reader.Session.ReferencedObjects.CurrentReferenceId,
            reader.Session.ReferencedTypes.GetReferencedType(1), reader.Session.ReferencedTypes.TryGetReferencedType(2, out _));
    }

    internal static KeyNotFoundException RequireOriginalFailure(NativeUnknownWellKnownHeaderObservation observation)
        => observation.Error as KeyNotFoundException ?? throw new InvalidOperationException(MissingFailure);
}

internal sealed record NativeUnknownWellKnownHeaderObservation(Field? Header, Exception? Error,
    long Position, long Remaining, uint ObjectReference, Type? FirstTypeReference, bool SecondTypeReference);
