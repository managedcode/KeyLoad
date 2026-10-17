using Orleans.Serialization.WireProtocol;

namespace KeyLoad.Features.InternalSerialization;

internal static class NativePayloadHeader
{
    internal static void Validate(Field field)
    {
        if (!field.HasFieldId || field.FieldIdDelta != NativeWireIdentities.FirstFieldId || field.WireType != WireType.TagDelimited
            || field.FieldType is not null && field.FieldType != typeof(NativePayload))
        {
            throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
        }
    }
}
