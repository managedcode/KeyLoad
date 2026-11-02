using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.Query;

/// <summary>Contains a JSON value used as a query operand.</summary>
/// <param name="Value">The serialized value.</param>
[method: JsonConstructor]
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.ValueOperand)]
public sealed record ValueOperand([property: Orleans.Id(0)] JsonElement Value) : Operand
{
    /// <summary>Serializes an object as the operand's JSON value.</summary>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The typed operand containing the serialized JSON value.</returns>
    public static ValueOperand Create<T>(T value) => new(JsonSerializer.SerializeToElement(value, JsonDefaults.Options));

    /// <summary>Creates a text or JSON null operand without erasing its declared type.</summary>
    /// <param name="value">Text, or null.</param>
    /// <returns>The JSON operand.</returns>
    public static ValueOperand Create(string? value) => new(JsonSerializer.SerializeToElement(value, JsonDefaults.Options));
}
