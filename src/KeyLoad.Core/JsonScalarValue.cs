namespace KeyLoad.Core;

/// <summary>Preserves a successfully read JSON scalar, including a JSON null value.</summary>
/// <param name="Value">The exact scalar value or canonical missing marker.</param>
public readonly record struct JsonScalarValue(KeyLoad.Storage.KeyComponent Value)
{
    /// <summary>Renders the scalar in a public JSON projection; missing fields render as JSON null.</summary>
    /// <returns>The native JSON node, or JSON null.</returns>
    public System.Text.Json.Nodes.JsonNode? ToNode() => Value.Kind switch
    {
        KeyLoad.Storage.KeyComponentKind.Null or KeyLoad.Storage.KeyComponentKind.Missing
            => System.Text.Json.Nodes.JsonValue.Create((string?)null),
        KeyLoad.Storage.KeyComponentKind.Text => System.Text.Json.Nodes.JsonValue.Create(Value.Text),
        KeyLoad.Storage.KeyComponentKind.Boolean => System.Text.Json.Nodes.JsonValue.Create(Value.Boolean),
        KeyLoad.Storage.KeyComponentKind.FixedPoint => System.Text.Json.Nodes.JsonValue.Create(Value.ExactNumber),
        _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, "This field is not a scalar value.")
    };
}
