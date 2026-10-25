using KeyLoad.Storage;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.FunctionalTests.StorageRecovery.Serialization;

/// <summary>Removes only the current generated StoreIdentity reader-capability field for a execution negative input.</summary>
internal static class IdentityCapabilityOmission
{
    private const uint CapabilityFieldId = 8;
    private const int CapabilityUnspecified = StoreReaderContract.Unspecified;
    private const int BytesAfterGeneratedRoot = 0;
    private const int FirstByte = 0;
    private const string InvalidIdentityPayload = "The current generated identity payload did not match its bounded wire contract.";

    internal static byte[] SerializeWithoutCapability(StoreIdentity identity, int maximumPayloadBytes)
    {
        var generated = OrleansSerialization.Serialize(identity with
        {
            MinimumReaderContract = CapabilityUnspecified
        });
        if (generated.Length > maximumPayloadBytes)
        {
            throw new InvalidDataException(InvalidIdentityPayload);
        }

        var context = OrleansSerialization.Serializer;
        ByteSpan? capability = null;
        using (var session = context.SessionPool.GetSession())
        {
            capability = FindCapabilityField(generated, session);
            ValidateCapabilitySpan(generated, capability);
        }

        var omitted = capability is { } field ? Remove(generated, field) : generated;
        VerifyCapabilityOmitted(omitted);

        var decoded = OrleansSerialization.Deserialize<StoreIdentity>(omitted);
        RequireSameCurrentIdentity(identity, decoded);
        return omitted;
    }

    internal static void VerifyCapabilityOmitted(ReadOnlySpan<byte> payload)
    {
        var context = OrleansSerialization.Serializer;
        using (var session = context.SessionPool.GetSession())
        {
            if (FindCapabilityField(payload, session) is not null)
            {
                throw new InvalidDataException(InvalidIdentityPayload);
            }
        }
        if (OrleansSerialization.Deserialize<StoreIdentity>(payload).MinimumReaderContract != CapabilityUnspecified)
        {
            throw new InvalidDataException(InvalidIdentityPayload);
        }
    }

    private static ByteSpan? FindCapabilityField(ReadOnlySpan<byte> bytes, global::Orleans.Serialization.Session.SerializerSession session)
    {
        var reader = Reader.Create(bytes, session);
        ReadCurrentIdentityHeader(ref reader);

        var id = FirstEnvelopeFieldId;
        var enteredMembers = false;
        ByteSpan? found = null;
        while (true)
        {
            var start = checked((int)reader.Position);
            var field = reader.ReadFieldHeader();
            if (field.IsEndObject)
            {
                if (!enteredMembers)
                {
                    throw new InvalidDataException(InvalidIdentityPayload);
                }
                break;
            }

            if (field.IsEndBaseFields && !enteredMembers)
            {
                enteredMembers = true;
                continue;
            }
            if (!enteredMembers || !field.HasFieldId)
            {
                throw new InvalidDataException(InvalidIdentityPayload);
            }
            id = checked(id + field.FieldIdDelta);
            if (id > CapabilityFieldId)
            {
                throw new InvalidDataException(InvalidIdentityPayload);
            }

            reader.SkipField(field);
            if (id == CapabilityFieldId)
            {
                if (field.WireType != WireType.VarInt || field.IsReference)
                {
                    throw new InvalidDataException(InvalidIdentityPayload);
                }
                var end = checked((int)reader.Position);
                if (!reader.ReadFieldHeader().IsEndObject)
                {
                    throw new InvalidDataException(InvalidIdentityPayload);
                }
                found = new(start, end);
                break;
            }
        }

        if (!reader.ReadFieldHeader().IsEndObject || reader.Remaining != BytesAfterGeneratedRoot)
        {
            throw new InvalidDataException(InvalidIdentityPayload);
        }
        return found;
    }

    private const uint FirstEnvelopeFieldId = 0;
    private const uint EnvelopeVersionFieldDelta = 0;
    private const uint EnvelopeValueFieldDelta = 1;

    private static void ReadCurrentIdentityHeader<TInput>(ref Reader<TInput> reader)
    {
        var header = reader.ReadFieldHeader();
        if (header.WireType != WireType.TagDelimited || header.IsReference
            || header.FieldIdDelta != FirstEnvelopeFieldId
            || header.FieldType is not null && header.FieldType != typeof(RuntimePayload))
        {
            throw new InvalidDataException(InvalidIdentityPayload);
        }
        var version = reader.ReadFieldHeader();
        if (!version.HasFieldId || version.FieldIdDelta != EnvelopeVersionFieldDelta
            || UInt32Codec.ReadValue(ref reader, version) != PayloadVersion.Current)
        {
            throw new InvalidDataException(InvalidIdentityPayload);
        }

        var value = reader.ReadFieldHeader();
        if (!value.HasFieldId || value.FieldIdDelta != EnvelopeValueFieldDelta
            || value.FieldType != typeof(StoreIdentity) || value.WireType != WireType.TagDelimited || value.IsReference)
        {
            throw new InvalidDataException(InvalidIdentityPayload);
        }

    }

    private static void ValidateCapabilitySpan(ReadOnlySpan<byte> bytes, ByteSpan? capability)
    {
        if (capability is { } field && (field.Start < FirstByte || field.End > bytes.Length || field.End <= field.Start))
        {
            throw new InvalidDataException(InvalidIdentityPayload);
        }
    }

    private static byte[] Remove(ReadOnlySpan<byte> source, ByteSpan field)
    {
        var result = new byte[checked(source.Length - (field.End - field.Start))];
        source[..field.Start].CopyTo(result);
        source[field.End..].CopyTo(result.AsSpan(field.Start));
        return result;
    }

    private static void RequireSameCurrentIdentity(StoreIdentity expected, StoreIdentity actual)
    {
        if (actual.MinimumReaderContract != CapabilityUnspecified || actual.FormatVersion != expected.FormatVersion
            || actual.KeyCodecVersion != expected.KeyCodecVersion || actual.NodeId != expected.NodeId
            || actual.Incarnation != expected.Incarnation || !actual.SigningKey.Span.SequenceEqual(expected.SigningKey.Span)
            || actual.Durability != expected.Durability || actual.DispatchPaused != expected.DispatchPaused
            || actual.ReadGeneration != expected.ReadGeneration)
        {
            throw new InvalidDataException(InvalidIdentityPayload);
        }
    }

    private readonly record struct ByteSpan(int Start, int End);
}
