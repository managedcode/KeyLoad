using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Validates the genuine bound native diagnostic policy.</summary>
public sealed class NativeComparisonDiagnosticOptionsValidator : IValidateOptions<NativeComparisonDiagnosticOptions>
{
    /// <summary>Checks all values before any formatter or native target is admitted.</summary>
    /// <param name="name">The native options instance name.</param>
    /// <param name="options">The bound diagnostic policy.</param>
    /// <returns>Success or the canonical validation failure.</returns>
    public ValidateOptionsResult Validate(string? name, NativeComparisonDiagnosticOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.IsValid() ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(NativeComparisonDiagnosticOptions.ValidationMessage);
    }
}
