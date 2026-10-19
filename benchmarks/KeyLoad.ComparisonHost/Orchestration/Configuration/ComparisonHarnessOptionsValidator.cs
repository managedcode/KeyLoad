using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkOrchestration;

internal sealed class ComparisonHarnessOptionsValidator : IValidateOptions<ComparisonHarnessOptions>
{
    public ValidateOptionsResult Validate(string? name, ComparisonHarnessOptions options)
        => options.IsValid() ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(ComparisonHarnessOptions.ValidationMessage);

}
