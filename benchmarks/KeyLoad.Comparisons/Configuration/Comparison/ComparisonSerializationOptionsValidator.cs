using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Validates actual bound execution SQL builder reservations.</summary>
public sealed class ComparisonSerializationOptionsValidator : IValidateOptions<ComparisonSerializationOptions>
{
    /// <summary>Checks policy before any execution target acquires resources.</summary>
    /// <param name="name">The execution options instance name.</param>
    /// <param name="options">The bound builder reservations.</param>
    /// <returns>Success or the canonical validation failure.</returns>
    public ValidateOptionsResult Validate(string? name, ComparisonSerializationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.IsValid() ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(ComparisonSerializationOptions.ValidationMessage);
    }
}
