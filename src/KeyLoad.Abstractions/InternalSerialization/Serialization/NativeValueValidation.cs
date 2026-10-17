using System.Reflection;

namespace KeyLoad.Features.InternalSerialization;

internal sealed record NativeValueValidation(bool Required, NativeValueValidation? Element,
    NativeValueValidation? DictionaryKey = null, NativeValueValidation? DictionaryValue = null)
{
    private const int DictionaryArgumentCount = 2;
    private const int SequenceArgumentCount = 1;
    private const int FirstTypeArgumentIndex = 0;
    private const int ValueTypeArgumentIndex = 1;
    internal static NativeValueValidation Create(NullabilityInfo info)
    {
        var required = !info.Type.IsValueType && info.ReadState == NullabilityState.NotNull;
        if (info.ElementType is { } element)
        {
            return new(required, Create(element));
        }
        // NullabilityInfo keeps Nullable<T> as Type, but its generic metadata already describes T.
        var arguments = info.GenericTypeArguments;
        var type = Nullable.GetUnderlyingType(info.Type) ?? info.Type;
        if (arguments.Length == DictionaryArgumentCount && (IsKeyValuePair(type) || IsDictionary(type) || type.GetInterfaces().Any(IsDictionary)))
        {
            return new(required, null, Create(arguments[FirstTypeArgumentIndex]), Create(arguments[ValueTypeArgumentIndex]));
        }
        return new(required, arguments.Length == SequenceArgumentCount ? Create(arguments[FirstTypeArgumentIndex]) : null);
    }

    private static bool IsDictionary(Type type)
        => type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                                  || type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));

    private static bool IsKeyValuePair(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);
}
