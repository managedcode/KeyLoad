using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.TestInfrastructure.Validation;

[ConfigurationBinding]
internal static class TestSuiteSelectionValidator
{
    private const string InvalidSuite = "The Aspire test suite is not supported.";
    private const string RetiredSelection = "Per-cell benchmark selectors are not supported by the test entry point.";
    private const string RetiredScaleProfileSetting = "KeyLoadTests:ScaleProfile";
    private const string RetiredVectorProfileSetting = "KeyLoadTests:VectorProfile";
    private const string RetiredOpenLoopRateSetting = "KeyLoadTests:OpenLoopRate";
    private const string RetiredOpenLoopCancellationProofSetting = "KeyLoadTests:OpenLoopCancellationProof";
    private static readonly string[] RetiredSettings =
    [
        RetiredScaleProfileSetting, RetiredVectorProfileSetting,
        RetiredOpenLoopRateSetting, RetiredOpenLoopCancellationProofSetting
    ];

    internal static bool HasRetiredBenchmarkSelection(IConfiguration configuration)
        => RetiredSettings.Any(key => configuration.AsEnumerable().Any(entry =>
            string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase)));

    internal static void RejectRetiredBenchmarkSelection(IConfiguration configuration)
    {
        if (HasRetiredBenchmarkSelection(configuration))
        {
            throw new InvalidOperationException(RetiredSelection);
        }
    }

    internal static void RejectRetiredBenchmarkArguments(string[] args)
    {
        foreach (var key in RetiredSettings)
        {
            var option = TestSuiteProtocol.ArgumentPrefix + key;
            if (args.Any(argument => string.Equals(argument, option, StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith(option + TestSuiteProtocol.ArgumentValueSeparator,
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(RetiredSelection);
            }
        }
    }

    internal static bool Requested(string[] args, string? suiteEnvironment)
    {
        RejectRetiredBenchmarkArguments(args);
        var option = TestSuiteProtocol.ArgumentPrefix + TestSuiteSettings.SuiteSetting;
        return !string.IsNullOrWhiteSpace(suiteEnvironment)
            || args.Any(argument => string.Equals(argument, option, StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith(option + TestSuiteProtocol.ArgumentValueSeparator,
                    StringComparison.OrdinalIgnoreCase));
    }

    internal static string ProjectForSuite(string suite)
        => suite switch
        {
            TestSuiteProtocol.AnalyzersSuite => TestSuiteProtocol.AnalyzerTestProjectName,
            TestSuiteProtocol.UnitSuite => TestSuiteProtocol.UnitTestProjectName,
            TestSuiteProtocol.RecoverySuite => TestSuiteProtocol.RecoveryTestProjectName,
            TestSuiteProtocol.ClusterSuite => TestSuiteProtocol.IntegrationTestProjectName,
            TestSuiteProtocol.ComparisonSuite => TestSuiteProtocol.ComparisonTestProjectName,
            TestSuiteProtocol.SiteSuite => TestSuiteProtocol.SiteTestProjectName,
            _ => throw new InvalidOperationException(InvalidSuite)
        };
}
