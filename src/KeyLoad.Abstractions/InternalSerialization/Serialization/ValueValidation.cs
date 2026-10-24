using System.Reflection;

namespace KeyLoad.InternalSerialization;

internal sealed record ValueValidation(bool Required, ValueValidation? Element,
    ValueValidation? DictionaryKey = null, ValueValidation? DictionaryValue = null)
{
    private const int DictionaryArgumentCount = 2;
    internal static ValueValidation Create(NullabilityInfo info)
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
            return new(required, null, Create(arguments[0]), Create(arguments[1]));
        }
        return new(required, arguments.Length == 1 ? Create(arguments[0]) : null);
    }

    private static bool IsDictionary(Type type)
        => type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                                  || type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));

    private static bool IsKeyValuePair(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);
}
