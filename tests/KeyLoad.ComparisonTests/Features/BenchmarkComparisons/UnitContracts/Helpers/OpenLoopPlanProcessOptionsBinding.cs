using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

[ConfigurationBinding]
internal static class OpenLoopPlanProcessOptionsBinding
{
    internal static IOptions<OpenLoopPlanProcessOptions> Capture()
    {
        var configured = new OpenLoopPlanProcessOptions();
        configured.Validate();
        return Options.Create(configured);
    }
}
