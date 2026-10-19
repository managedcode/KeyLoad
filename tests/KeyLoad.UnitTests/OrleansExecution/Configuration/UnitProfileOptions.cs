using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

internal static class UnitProfileOptions
{
    internal static IOptions<ClusterProfileExecutionOptions> Execution(ClusterProfileExecutionOptions? configured = null)
    {
        var value = configured ?? new ClusterProfileExecutionOptions();
        if (!value.IsValid())
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(ClusterProfileExecutionOptions), [ClusterProfileExecutionOptions.ValidationMessage]);
        }
        return Options.Create(value);
    }
}
