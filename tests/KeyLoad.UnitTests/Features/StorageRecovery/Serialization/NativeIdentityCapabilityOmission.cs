using KeyLoad.Features.InternalSerialization;
using KeyLoad.Storage;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests.Features.StorageRecovery.Serialization;

/// <summary>Removes only the current generated StoreIdentity reader-capability field for a native negative input.</summary>
internal static class NativeIdentityCapabilityOmission
{
    private const uint CapabilityFieldId = 8;
    private const int CapabilityUnspecified = StoreReaderContract.Unspecified;
    private const int BytesAfterGeneratedRoot = 0;
    private const int FirstByte = 0;
    private const string InvalidNativeIdentityPayload = "The current generated identity payload did not match its bounded wire contract.";

    internal static byte[] SerializeWithoutCapability(StoreIdentity identity, int maximumPayloadBytes)
    {
        var generated = NativeSerialization.Serialize(identity with
        { MinimumReaderContract = CapabilityUnspecified });
        if (generated.Length > maximumPayloadBytes)
        {
            throw new InvalidDataException(InvalidNativeIdentityPayload);
        }

        var context = NativeSerializerProviders.Get(typeof(StoreIdentity));
        ByteSpan? capability = null;
        using (var session = context.Sessions.GetSession())
        {
            capability = FindCapabilityField(generated, session);
            ValidateCapabilitySpan(generated, capability);
        }

        var omitted = capability is { } field ? Remove(generated, field) : generated;
        VerifyCapabilityOmitted(omitted);

        var decoded = NativeSerialization.Deserialize<StoreIdentity>(omitted);
        RequireSameCurrentIdentity(identity, decoded);
        return omitted;
    }

    internal static void VerifyCapabilityOmitted(ReadOnlySpan<byte> payload)
    {
        var context = NativeSerializerProviders.Get(typeof(StoreIdentity));
        using (var session = context.Sessions.GetSession())
        {
            if (FindCapabilityField(payload, session) is not null)
            {
                throw new InvalidDataException(InvalidNativeIdentityPayload);
            }
        }
        if (NativeSerialization.Deserialize<StoreIdentity>(payload).MinimumReaderContract != CapabilityUnspecified)
        {
            throw new InvalidDataException(InvalidNativeIdentityPayload);
        }
    }

    private static ByteSpan? FindCapabilityField(ReadOnlySpan<byte> bytes, global::Orleans.Serialization.Session.SerializerSession session)
    {
        var reader = Reader.Create(bytes, session);
        ReadCurrentIdentityHeader(ref reader);

        var id = NativeWireIdentities.FirstFieldId;
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
                    throw new InvalidDataException(InvalidNativeIdentityPayload);
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
                throw new InvalidDataException(InvalidNativeIdentityPayload);
            }
            id = checked(id + field.FieldIdDelta);
            if (id > CapabilityFieldId)
            {
                throw new InvalidDataException(InvalidNativeIdentityPayload);
            }

            reader.SkipField(field);
            if (id == CapabilityFieldId)
            {
                if (field.WireType != WireType.VarInt || field.IsReference)
                {
                    throw new InvalidDataException(InvalidNativeIdentityPayload);
                }
                var end = checked((int)reader.Position);
                if (!reader.ReadFieldHeader().IsEndObject)
                {
                    throw new InvalidDataException(InvalidNativeIdentityPayload);
                }
                found = new(start, end);
                break;
            }
        }

        if (!reader.ReadFieldHeader().IsEndObject || reader.Remaining != BytesAfterGeneratedRoot)
        {
            throw new InvalidDataException(InvalidNativeIdentityPayload);
        }
        return found;
    }

    private static void ReadCurrentIdentityHeader<TInput>(ref Reader<TInput> reader)
    {
        NativePayloadHeader.Validate(reader.ReadFieldHeader());
        var version = reader.ReadFieldHeader();
        if (!version.HasFieldId || version.FieldIdDelta != NativeWireIdentities.VersionFieldDelta
            || UInt32Codec.ReadValue(ref reader, version) != NativePayloadVersion.Current)
        {
            throw new InvalidDataException(InvalidNativeIdentityPayload);
        }

        var value = reader.ReadFieldHeader();
        if (!value.HasFieldId || value.FieldIdDelta != NativeWireIdentities.ValueFieldDelta
            || value.FieldType != typeof(StoreIdentity) || value.WireType != WireType.TagDelimited || value.IsReference)
        {
            throw new InvalidDataException(InvalidNativeIdentityPayload);
        }

    }

    private static void ValidateCapabilitySpan(ReadOnlySpan<byte> bytes, ByteSpan? capability)
    {
        if (capability is { } field && (field.Start < FirstByte || field.End > bytes.Length || field.End <= field.Start))
        {
            throw new InvalidDataException(InvalidNativeIdentityPayload);
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
            throw new InvalidDataException(InvalidNativeIdentityPayload);
        }
    }

    private readonly record struct ByteSpan(int Start, int End);
}
