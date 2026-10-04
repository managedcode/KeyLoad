using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.Features.ResourceExecution;

internal sealed class StrictImmutableArrayJsonConverterFactory : JsonConverterFactory
{
    private static readonly MethodInfo CreateMethod = typeof(StrictImmutableArrayJsonConverterFactory)
        .GetMethod(nameof(CreateTypedConverter), BindingFlags.NonPublic | BindingFlags.Static)!;

    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(ImmutableArray<>);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return (JsonConverter)CreateMethod.MakeGenericMethod(typeToConvert.GetGenericArguments()).Invoke(null, null)!;
    }

    private static StrictImmutableArrayJsonConverter<T> CreateTypedConverter<T>() => new();
}
