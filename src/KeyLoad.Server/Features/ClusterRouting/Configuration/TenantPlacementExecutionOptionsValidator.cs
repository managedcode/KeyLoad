using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class TenantPlacementExecutionOptionsValidator : IValidateOptions<TenantPlacementExecutionOptions>
{
    internal const string Invalid = "The bounded tenant placement execution settings are invalid.";

    public ValidateOptionsResult Validate(string? name, TenantPlacementExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            options.Validate();
            return ValidateOptionsResult.Success;
        }
        catch (ArgumentOutOfRangeException) { return ValidateOptionsResult.Fail(Invalid); }
    }
}
