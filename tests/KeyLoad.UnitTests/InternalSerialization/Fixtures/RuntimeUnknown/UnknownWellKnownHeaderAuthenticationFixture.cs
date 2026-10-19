using KeyLoad.Clustering;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

// Copy the genuine auth writer's bytes, replacing only the located header's metadata.
internal static class UnknownWellKnownHeaderAuthenticationFixture
{
    private const string UnexpectedString = "The genuine authentication string must use execution string/reference syntax.";

    internal static byte[] Encode(byte[] source, UnknownWellKnownHeaderAuthenticationScope scope, bool malformed)
    {
        using var session = SerializerProviders.Get(typeof(GrainValue)).Sessions.GetSession();
        var located = Locate(source, session, scope);
        var writer = Writer.CreatePooled(session);
        try
        {
            writer.Write(source.AsSpan(0, located.Start));
            if (malformed)
            {
                UnknownWellKnownHeaderFixture.Unknown(ref writer, located.Field.FieldIdDelta, located.Field.WireType);
            }
            else
            {
                writer.Write(source.AsSpan(located.Start, located.End - located.Start));
            }
            writer.Write(source.AsSpan(located.End));
            writer.Commit();
            return writer.Output.ToArray();
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static (int Start, int End, Field Field) Locate(byte[] source,
        global::Orleans.Serialization.Session.SerializerSession session, UnknownWellKnownHeaderAuthenticationScope scope)
    {
        var reader = Reader.Create(source.AsSpan(), session);
        var located = Take(ref reader);
        if (scope == UnknownWellKnownHeaderAuthenticationScope.Envelope)
        {
            return located;
        }
        var version = reader.ReadFieldHeader();
        _ = UInt32Codec.ReadValue(ref reader, version);
        located = Take(ref reader);
        if (scope == UnknownWellKnownHeaderAuthenticationScope.Value)
        {
            return located;
        }
        _ = reader.ReadFieldHeader(); // GrainValue constructor/body scope fence.
        _ = reader.ReadFieldHeader(); // Principal value header.
        _ = reader.ReadFieldHeader(); // Principal constructor/body scope fence.
        located = Take(ref reader);
        if (scope == UnknownWellKnownHeaderAuthenticationScope.Member)
        {
            return located;
        }
        SkipString(ref reader, located.Field);
        var tenant = reader.ReadFieldHeader();
        SkipString(ref reader, tenant);
        _ = reader.ReadFieldHeader(); // ImmutableArray grants surrogate.
        _ = reader.ReadFieldHeader(); // Backing ScopeGrant[] field.
        return Take(ref reader);
    }

    private static (int Start, int End, Field Field) Take(ref Reader<SpanReaderInput> reader)
    {
        var start = checked((int)reader.Position);
        var field = reader.ReadFieldHeader();
        return (start, checked((int)reader.Position), field);
    }

    private static void SkipString(ref Reader<SpanReaderInput> reader, Field field)
    {
        if (field.IsReference)
        {
            _ = reader.ReadVarUInt32();
        }
        else if (field.WireType == WireType.LengthPrefixed)
        {
            reader.Skip(reader.ReadVarUInt32());
        }
        else
        {
            throw new InvalidOperationException(UnexpectedString);
        }
    }
}
