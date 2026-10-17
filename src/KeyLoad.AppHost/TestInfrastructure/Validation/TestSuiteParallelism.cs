using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.TestInfrastructure.Validation;

[ConfigurationBinding]
internal static class TestSuiteParallelism
{
    private const int ComparisonParallelTests = 1;
    private const string ParallelismSetting = TestExecutionOptions.SectionName + ":" + nameof(TestExecutionOptions.MaximumParallelTests);
    private const string ComparisonParallelismMessage = "Comparison measurements require exactly one native test at a time.";

    internal static int Read(IConfiguration configuration, TestExecutionOptions execution, string suite)
    {
        if (suite != TestSuiteProtocol.ComparisonSuite)
        {
            return execution.MaximumParallelTests;
        }
        if (configuration[ParallelismSetting] is not null
            && execution.MaximumParallelTests != ComparisonParallelTests)
        {
            throw new InvalidOperationException(ComparisonParallelismMessage);
        }
        return ComparisonParallelTests;
    }
}
