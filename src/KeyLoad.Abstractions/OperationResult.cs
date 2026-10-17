using System.Text.Json.Serialization;

namespace KeyLoad;

/// <summary>Carries a serialized operation result or a safe domain failure.</summary>
/// <param name="Json">Contains the JSON representation of the associated value.</param>
/// <param name="Error">Contains the domain failure when rejected.</param>
/// <param name="SafeDetail">Contains safe caller-visible failure detail.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.OperationResult)]
public sealed record OperationResult([property: Orleans.Id(0)] string? Json, [property: Orleans.Id(1)] ErrorCode? Error = null, [property: Orleans.Id(2)] string? SafeDetail = null)
{
    /// <summary>Gets the owned typed result for native internal dispatch.</summary>
    [Orleans.Id(3)]
    [JsonIgnore]
    public object? NativeValue
    {
        get; init;
    }

    private const string WrongNativeResultMessage = "The internal operation result type is invalid.";
    private const string RejectedOperationMessage = "The operation was rejected.";
    /// <summary>Deserializes the successful result or throws the stored domain failure.</summary>
    /// <typeparam name="T">Specifies the type of result.</typeparam>
    /// <returns>The requested value.</returns>
    public T Get<T>()
    {
        if (Error is { } code)
        {
            throw Errors.Fail(code, SafeDetail ?? RejectedOperationMessage);
        }
        if (NativeValue is T value)
        {
            return value;
        }
        throw Errors.Fail(ErrorCode.Corruption, WrongNativeResultMessage);
    }
}
