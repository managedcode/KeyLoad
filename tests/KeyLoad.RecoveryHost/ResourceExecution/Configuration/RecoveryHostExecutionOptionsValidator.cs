using Microsoft.Extensions.Options;

namespace KeyLoad.RecoveryHost;

internal sealed class RecoveryHostExecutionOptionsValidator : IValidateOptions<RecoveryHostExecutionOptions>
{
    public ValidateOptionsResult Validate(string? name, RecoveryHostExecutionOptions options)
        => options.IsValid() ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(RecoveryHostExecutionOptions.ValidationMessage);
}
