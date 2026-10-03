using System.Text.Json;

namespace KeyLoad.Features.InternalSerialization;

// System.Text.Json owns DOM construction; Orleans generates the persisted structural encoding.
[Orleans.RegisterConverter]
internal sealed class JsonElementSurrogateConverter : Orleans.IConverter<JsonElement, JsonElementSurrogate>
{
    public JsonElementSurrogate ConvertToSurrogate(in JsonElement value)
        => new() { Root = NativeDomProjection.Create(value) };

    public JsonElement ConvertFromSurrogate(in JsonElementSurrogate surrogate)
    {
        try
        {
            NativeDomPreflight.Validate(surrogate.Root);
            return NativeDomWriter.Materialize(surrogate.Root!);
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
        }
    }

}
