using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

internal static class UnitRequestProbeOptions
{
    internal static readonly IOptions<RequestProbeExecutionOptions> Execution = Create();
    internal static readonly RequestCqrsProbeJson Json = new(Execution);

    private static IOptions<RequestProbeExecutionOptions> Create()
    {
        var value = new RequestProbeExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }
}
