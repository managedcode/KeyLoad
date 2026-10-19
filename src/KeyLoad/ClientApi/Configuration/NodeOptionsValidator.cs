using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Rejects invalid node identity, topology and nested limits before physical ownership starts.</summary>
internal sealed class NodeOptionsValidator : IValidateOptions<NodeOptions>
{
    public ValidateOptionsResult Validate(string? name, NodeOptions options)
    {
        try
        {
            options.Validate();
            return ValidateOptionsResult.Success;
        }
        catch (Exception failure) when (failure is ArgumentException or InvalidOperationException)
        {
            return ValidateOptionsResult.Fail(failure.Message);
        }
    }
}
