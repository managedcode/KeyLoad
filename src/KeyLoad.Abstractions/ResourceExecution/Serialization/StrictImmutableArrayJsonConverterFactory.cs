using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.ResourceExecution;

internal sealed class StrictImmutableArrayJsonConverterFactory : JsonConverterFactory
{
    private static readonly MethodInfo CreateMethod = typeof(StrictImmutableArrayJsonConverterFactory)
        .GetMethod(nameof(CreateTypedConverter), BindingFlags.NonPublic | BindingFlags.Static)!;

    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert) /* KLD0041: The native converter-factory contract supplies a Type reference whose closed immutable-array shape is inspected here. */;
        return typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(ImmutableArray<>);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert) /* KLD0041: The CLR guard requires the runtime closed collection Type before selecting its native typed converter. */;
        return (JsonConverter)CreateMethod.MakeGenericMethod(typeToConvert.GetGenericArguments()).Invoke(null, null) /* KLD0041: JsonConverterFactory supplies T only at runtime; the native reflection call constructs its existing closed typed converter. */! /* KLD0042: The reflected method returns StrictImmutableArrayJsonConverter<T>, which derives from the required native JsonConverter base. */;
    }

    private static StrictImmutableArrayJsonConverter<T> CreateTypedConverter<T>() => new();
}
