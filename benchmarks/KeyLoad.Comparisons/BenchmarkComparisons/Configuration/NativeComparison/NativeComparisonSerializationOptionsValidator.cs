using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Validates actual bound native SQL builder reservations.</summary>
public sealed class NativeComparisonSerializationOptionsValidator : IValidateOptions<NativeComparisonSerializationOptions>
{
    /// <summary>Checks policy before any native target acquires resources.</summary>
    /// <param name="name">The native options instance name.</param>
    /// <param name="options">The bound builder reservations.</param>
    /// <returns>Success or the canonical validation failure.</returns>
    public ValidateOptionsResult Validate(string? name, NativeComparisonSerializationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.IsValid() ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(NativeComparisonSerializationOptions.ValidationMessage);
    }
}
