using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal sealed record NativeCoverageImageTestOptions(
    IOptions<NativeCoverageExecutionOptions> Coverage,
    IOptions<TestExecutionOptions> Execution)
{
    internal static NativeCoverageImageTestOptions Capture()
    {
        using var configuration = new ConfigurationManager();
        configuration.AddEnvironmentVariables();
        return new(AppHostOptionsRegistration.BindNativeCoverage(configuration),
            AppHostOptionsRegistration.BindTestExecution(configuration));
    }
}
