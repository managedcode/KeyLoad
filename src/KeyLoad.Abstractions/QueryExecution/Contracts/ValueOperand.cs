using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.Query;

/// <summary>Contains a JSON value used as a query operand.</summary>
/// <param name="Value">The serialized value.</param>
[method: JsonConstructor]
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ValueOperand)]
public sealed record ValueOperand([property: Orleans.Id(0)] JsonElement Value) : Operand
{
    /// <summary>Serializes an object as the operand's JSON value.</summary>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The typed operand containing the serialized JSON value.</returns>
    public static ValueOperand Create(object? value) => new(JsonSerializer.SerializeToElement(value, JsonDefaults.Options));
}
