namespace KeyLoad.Core;

/// <summary>Preserves a successfully read JSON scalar, including a JSON null value.</summary>
/// <param name="Value">The exact scalar value or canonical missing marker.</param>
public readonly record struct JsonScalarValue(object? Value);
