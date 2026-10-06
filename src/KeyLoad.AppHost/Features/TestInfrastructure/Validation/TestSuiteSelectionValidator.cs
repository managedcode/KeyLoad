using System.Globalization;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.TestInfrastructure.Validation;

[ConfigurationBinding]
internal static class TestSuiteSelectionValidator
{
    private const int ReadOpenLoopRateEmptyCount = 0;

    internal const string OpenLoopRateSetting = TestSuiteProtocol.OpenLoopRateSetting;
    internal const string OpenLoopRateEnvironment = TestSuiteProtocol.OpenLoopRateEnvironment;
    internal const string OpenLoopNativeRateEnvironment = TestSuiteProtocol.OpenLoopNativeRateEnvironment;
    internal const string OpenLoopCancellationProofSetting = TestSuiteProtocol.OpenLoopCancellationProofSetting;
    internal const string MeasuredOpenLoopFilter = "/*/*/IsolatedNativeOpenLoopComparisonTests/*";
    internal const string CancellationProofFilter = "/*/*/IsolatedNativeOpenLoopCancellationTests/*";
    private const string InvalidOpenLoop = "The open-loop test selection is invalid.";
    private const string InvalidProof = "The open-loop cancellation-proof selection is invalid.";
    private const string InvalidVectorProfile = "The vector-profile test selection is invalid.";
    private const string InvalidScaleProfile = "The scale-profile test selection is invalid.";
    private const string InvalidSuite = "The Aspire test suite is not supported.";

    internal static bool Requested(string[] args, string? suiteEnvironment, string? vectorEnvironment,
        string? scaleEnvironment, string? openLoopEnvironment)
    {
        var vectorOption = TestSuiteProtocol.ArgumentPrefix + TestSuiteSettings.VectorProfileSetting;
        var vectorAssignment = vectorOption + TestSuiteProtocol.ArgumentValueSeparator;
        var openLoopOption = TestSuiteProtocol.ArgumentPrefix + OpenLoopRateSetting;
        var openLoopAssignment = openLoopOption + TestSuiteProtocol.ArgumentValueSeparator;
        var scaleOption = TestSuiteProtocol.ArgumentPrefix + TestSuiteSettings.ScaleProfileSetting;
        var scaleAssignment = scaleOption + TestSuiteProtocol.ArgumentValueSeparator;
        if (args.Any(argument => argument == vectorOption || argument == vectorAssignment))
        {
            throw new InvalidOperationException(InvalidVectorProfile);
        }
        if (args.Any(argument => argument == openLoopOption || argument == openLoopAssignment))
        {
            throw new InvalidOperationException(InvalidOpenLoop);
        }
        if (args.Any(argument => argument == scaleOption || argument == scaleAssignment))
        {
            throw new InvalidOperationException(InvalidScaleProfile);
        }

        var suiteOption = TestSuiteProtocol.ArgumentPrefix + TestSuiteSettings.SuiteSetting;
        return !string.IsNullOrWhiteSpace(suiteEnvironment)
            || !string.IsNullOrWhiteSpace(vectorEnvironment)
            || !string.IsNullOrWhiteSpace(scaleEnvironment)
            || !string.IsNullOrEmpty(openLoopEnvironment)
            || args.Any(argument => argument == suiteOption
                || argument.StartsWith(suiteOption + TestSuiteProtocol.ArgumentValueSeparator, StringComparison.Ordinal)
                || argument == scaleOption || argument.StartsWith(scaleAssignment, StringComparison.Ordinal)
                || argument == openLoopOption || argument.StartsWith(openLoopAssignment, StringComparison.Ordinal)
                || argument == vectorOption || argument.StartsWith(vectorAssignment, StringComparison.Ordinal));
    }

    internal static string ProjectForSuite(string suite)
        => suite switch
        {
            TestSuiteProtocol.AnalyzersSuite => TestSuiteProtocol.AnalyzerTestProjectName,
            TestSuiteProtocol.UnitSuite or TestSuiteProtocol.ScalarUnitSuite => TestSuiteProtocol.UnitTestProjectName,
            TestSuiteProtocol.RecoverySuite => TestSuiteProtocol.RecoveryTestProjectName,
            TestSuiteProtocol.Rf3Suite => TestSuiteProtocol.IntegrationTestProjectName,
            TestSuiteProtocol.ComparisonSuite => TestSuiteProtocol.ComparisonTestProjectName,
            TestSuiteProtocol.SiteSuite => TestSuiteProtocol.SiteTestProjectName,
            _ => throw new InvalidOperationException(InvalidSuite)
        };

    internal static int? ReadOpenLoopRate(string? value, string? nativeRate, string? suite, string? filter,
        bool nativeCancellationProofSelected)
    {
        if (value is null || value.Length == ReadOpenLoopRateEmptyCount)
        {
            if (!string.IsNullOrWhiteSpace(suite) && (nativeRate is not null || nativeCancellationProofSelected))
            {
                throw new InvalidOperationException(InvalidOpenLoop);
            }
            return null;
        }

        if (suite != TestSuiteProtocol.ComparisonSuite || nativeRate is not null || nativeCancellationProofSelected
            || string.IsNullOrWhiteSpace(value)
            || filter is not (MeasuredOpenLoopFilter or CancellationProofFilter)
            || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var rate)
            || !OpenLoopRateContract.AcceptedRates.Contains(rate)
            || !string.Equals(value, rate.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(InvalidOpenLoop);
        }
        return rate;
    }

    internal static string ExpectedScaleFilter(int? rate, string? filter)
        => rate is null ? TestSuiteProtocol.IsolatedComparisonFilter : filter!;

    internal static void ValidateOpenLoop(int? rate, string suite, string? target, string? filter,
        ScaledComparisonProfile? scaleProfile, bool vectorSelected, bool vectorSelectorPresent, bool overrides,
        string? appHostProfile, string? nodeCount, string? scenario)
    {
        if (rate is null)
        {
            return;
        }
        if (suite != TestSuiteProtocol.ComparisonSuite || scaleProfile is null || vectorSelected
            || vectorSelectorPresent || appHostProfile is { } selectedProfile
                && !string.Equals(selectedProfile, TestSuiteProtocol.GeneralComparisonAppHostProfile,
                    StringComparison.OrdinalIgnoreCase)
            || overrides)
        {
            throw new InvalidOperationException(InvalidOpenLoop);
        }
        if (filter == CancellationProofFilter
            && (target != OpenLoopProtocolIdentities.KeyLoadTarget
                || nodeCount != OpenLoopExecutionOptions.DefaultMaximumNodes.ToString(CultureInfo.InvariantCulture)
                || scenario != nameof(Scenario.PointRead)))
        {
            throw new InvalidOperationException(InvalidProof);
        }
    }

    internal static ScaledComparisonProfile? ReadScaleProfile(IConfiguration configuration, string suite,
        string? target, string expectedFilter, bool workloadOverrides)
    {
        var value = configuration[TestSuiteSettings.ScaleProfileSetting];
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        if (suite != TestSuiteProtocol.ComparisonSuite || target is null
            || !IsolatedComparisonContract.Current.Targets.Contains(target, StringComparer.Ordinal)
            || configuration.GetValue<bool>(TestSuiteSettings.BenchmarkEnabledSetting)
            || configuration[TestSuiteSettings.TimeoutSetting] is { } timeout
                && timeout != TestSuiteProtocol.ProfileTimeoutMinutesText
            || configuration[ComparisonWorkerSelection.ProfileSetting] is not { } evidenceProfile
            || configuration[ComparisonWorkerSelection.ScaleProfileSetting] is not null
            || configuration[TestSuiteSettings.FilterSetting] != expectedFilter
            || !HasValidScaleWorkload(configuration)
            || workloadOverrides)
        {
            throw new InvalidOperationException(InvalidScaleProfile);
        }
        try
        {
            var profile = ScaledComparisonProfileParser.Parse(value);
            if (!string.Equals(profile.Id, evidenceProfile, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(InvalidScaleProfile);
            }
            return profile;
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new InvalidOperationException(InvalidScaleProfile);
        }
    }

    internal static bool HasValidScaleWorkload(IConfiguration configuration)
    {
        if (!int.TryParse(configuration[ComparisonWorkerSelection.NodeCountSetting], NumberStyles.None,
                CultureInfo.InvariantCulture, out var nodeCount)
            || !IsolatedComparisonContract.Current.NodeCounts.Contains(nodeCount))
        {
            return false;
        }
        var text = configuration[ComparisonWorkerSelection.ScenarioSetting];
        if (!Enum.TryParse<Scenario>(text, out var scenario) || !Enum.IsDefined(scenario)
            || !string.Equals(text, scenario.ToString(), StringComparison.Ordinal))
        {
            return false;
        }
        return scenario is Scenario.PointRead or Scenario.DocumentWrite
            or Scenario.DocumentUpdate or Scenario.DocumentDelete;
    }
}
