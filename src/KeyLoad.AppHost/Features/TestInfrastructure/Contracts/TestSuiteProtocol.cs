namespace KeyLoad.AppHost.Features.TestInfrastructure;

internal static class TestSuiteProtocol
{
    internal const string AnalyzersSuite = "analyzers";
    internal const string UnitSuite = "unit";
    internal const string ScalarUnitSuite = "unit-scalar";
    internal const string RecoverySuite = "recovery";
    internal const string Rf3Suite = "rf3";
    internal const string ComparisonSuite = "comparison";
    internal const string SiteSuite = "site";
    internal const string ProfileTimeoutMinutesText = "140";
    internal const string IsolatedComparisonFilter = "/*/*/IsolatedNativeComparisonTests/*";
    internal const string ArgumentPrefix = "--";
    internal const string ArgumentValueSeparator = "=";
}
