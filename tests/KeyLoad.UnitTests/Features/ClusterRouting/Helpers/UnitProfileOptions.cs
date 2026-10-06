using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

internal static class UnitProfileOptions
{
    internal static IOptions<ClusterProfileExecutionOptions> Execution(ClusterProfileExecutionOptions? configured = null)
    {
        var value = configured ?? new ClusterProfileExecutionOptions();
        if (!value.IsValid()) { throw new OptionsValidationException(Options.DefaultName, typeof(ClusterProfileExecutionOptions), [ClusterProfileExecutionOptions.ValidationMessage]); }
        return Options.Create(value);
    }
    internal static IOptions<RequestProbeFileOptions> ProbeFiles(RequestProbeFileOptions? configured = null)
    {
        var value = configured ?? new RequestProbeFileOptions();
        if (!value.IsValid()) { throw new OptionsValidationException(Options.DefaultName, typeof(RequestProbeFileOptions), [RequestProbeFileOptions.ValidationMessage]); }
        return Options.Create(value);
    }
}
