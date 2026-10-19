using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Validates the genuine bound execution diagnostic policy.</summary>
public sealed class ComparisonDiagnosticOptionsValidator : IValidateOptions<ComparisonDiagnosticOptions>
{
    /// <summary>Checks all values before any formatter or execution target is admitted.</summary>
    /// <param name="name">The execution options instance name.</param>
    /// <param name="options">The bound diagnostic policy.</param>
    /// <returns>Success or the canonical validation failure.</returns>
    public ValidateOptionsResult Validate(string? name, ComparisonDiagnosticOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.IsValid() ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(ComparisonDiagnosticOptions.ValidationMessage);
    }
}
