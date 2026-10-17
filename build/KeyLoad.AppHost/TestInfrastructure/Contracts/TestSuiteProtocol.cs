namespace KeyLoad.AppHost.Features.TestInfrastructure;

internal static class TestSuiteProtocol
{
    internal const string AnalyzersSuite = "analyzers";
    internal const string UnitSuite = "unit";
    internal const string RecoverySuite = "recovery";
    internal const string Rf3Suite = "rf3";
    internal const string ComparisonSuite = "comparison";
    internal const string SiteSuite = "site";
    internal const string AnalyzerTestProjectName = "KeyLoad.Analyzers.Tests";
    internal const string UnitTestProjectName = "KeyLoad.UnitTests";
    internal const string RecoveryTestProjectName = "KeyLoad.RecoveryTests";
    internal const string IntegrationTestProjectName = "KeyLoad.IntegrationTests";
    internal const string ComparisonTestProjectName = "KeyLoad.ComparisonTests";
    internal const string SiteTestProjectName = "KeyLoad.SiteTests";
    internal const string SuiteSetting = "KeyLoadTests:Suite";
    internal const string SuiteEnvironment = "KeyLoadTests__Suite";
    internal const string FilterSetting = "KeyLoadTests:Filter";
    internal const string TimeoutSetting = "KeyLoadTests:TimeoutMinutes";
    internal const string BenchmarkConfigurationSection = "Benchmarks";
    internal const string ArgumentPrefix = "--";
    internal const string ArgumentValueSeparator = "=";
}
