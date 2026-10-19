using Orleans.Serialization.WireProtocol;

namespace KeyLoad.Features.InternalSerialization;

internal static class PayloadHeader
{
    internal static void Validate(Field field)
    {
        if (!field.HasFieldId || field.FieldIdDelta != WireIdentities.FirstFieldId || field.WireType != WireType.TagDelimited
            || field.FieldType is not null && field.FieldType != typeof(RuntimePayload))
        {
            throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
        }
    }
}
