using KeyLoad.AppHost.Features.ClusterReplication;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests;

internal static class IntegrationProfileOptions
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
