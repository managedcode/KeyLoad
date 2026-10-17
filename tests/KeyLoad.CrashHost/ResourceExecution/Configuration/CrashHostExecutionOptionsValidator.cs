using Microsoft.Extensions.Options;

namespace KeyLoad.CrashHost;

internal sealed class CrashHostExecutionOptionsValidator : IValidateOptions<CrashHostExecutionOptions>
{
    public ValidateOptionsResult Validate(string? name, CrashHostExecutionOptions options)
        => options.IsValid() ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(CrashHostExecutionOptions.ValidationMessage);
}
