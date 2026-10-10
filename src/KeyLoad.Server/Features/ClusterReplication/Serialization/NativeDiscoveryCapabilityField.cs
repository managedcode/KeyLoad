using KeyLoad.Features.InternalSerialization;
using KeyLoad.Orleans;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Session;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.Server;

internal static class NativeDiscoveryCapabilityField
{
    private const uint CapabilityId = 7;
    private const string InvalidCurrentDiscovery = "The protected current discovery input does not match its generated wire contract.";

    internal static Span? Find(ReadOnlySpan<byte> bytes, SerializerSession session)
    {
        var reader = Reader.Create(bytes, session);
        RequireRoot(ref reader);
        var id = NativeWireIdentities.FirstFieldId;
        var enteredMembers = false;
        Span? result = null;
        while (true)
        {
            var start = checked((int)reader.Position);
            var field = reader.ReadFieldHeader();
            if (field.IsEndObject)
            {
                if (!enteredMembers)
                { throw Invalid(); }
                break;
            }
            if (field.IsEndBaseFields && !enteredMembers)
            { enteredMembers = true; continue; }
            if (!enteredMembers || !field.HasFieldId)
            { throw Invalid(); }
            id = checked(id + field.FieldIdDelta);
            if (id > CapabilityId)
            { throw Invalid(); }
            reader.SkipField(field);
            if (id != CapabilityId)
            { continue; }
            if (field.WireType != WireType.VarInt || field.IsReference)
            { throw Invalid(); }
            result = new(start, checked((int)reader.Position));
            if (!reader.ReadFieldHeader().IsEndObject)
            { throw Invalid(); }
            break;
        }
        if (!reader.ReadFieldHeader().IsEndObject || reader.Remaining != NativeWireIdentities.EmptyRemainingBytes)
        { throw Invalid(); }
        return result;
    }

    private static void RequireRoot<TInput>(ref Reader<TInput> reader)
    {
        NativePayloadHeader.Validate(reader.ReadFieldHeader());
        var version = reader.ReadFieldHeader();
        if (!version.HasFieldId || version.FieldIdDelta != NativeWireIdentities.VersionFieldDelta
            || UInt32Codec.ReadValue(ref reader, version) != NativePayloadVersion.Current)
        { throw Invalid(); }
        var value = reader.ReadFieldHeader();
        if (!value.HasFieldId || value.FieldIdDelta != NativeWireIdentities.ValueFieldDelta
            || value.FieldType != typeof(ReplicaSiloDiscovery) || value.WireType != WireType.TagDelimited
            || value.IsReference)
        { throw Invalid(); }
    }

    internal static InvalidDataException Invalid() => new(InvalidCurrentDiscovery);
    internal readonly record struct Span(int Start, int End)
    { internal int Length => checked(End - Start); }
}
