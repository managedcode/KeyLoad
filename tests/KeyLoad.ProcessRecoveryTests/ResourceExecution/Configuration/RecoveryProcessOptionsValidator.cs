using Microsoft.Extensions.Options;

namespace KeyLoad.ProcessRecoveryTests;

internal sealed class RecoveryProcessOptionsValidator : IValidateOptions<RecoveryProcessOptions>
{
    public ValidateOptionsResult Validate(string? name, RecoveryProcessOptions options)
        => options.IsValid() ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(RecoveryProcessOptions.ValidationMessage);
}
