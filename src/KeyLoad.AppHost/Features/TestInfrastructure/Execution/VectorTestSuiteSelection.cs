using KeyLoad;
using System.Globalization;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

[ConfigurationBinding]
internal static class VectorTestSuiteSelection
{
    internal static VectorComparisonProfile? Read(IConfiguration configuration, string suite, string? target, bool overrides)
    {
        const string MessageText = "The vector-profile test selection is invalid.";

        var value = configuration[TestSuiteSettings.VectorProfileSetting];
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (suite != TestSuiteProtocol.ComparisonSuite || target is null
            || !IsolatedComparisonContract.Current.Targets.Contains(target, StringComparer.Ordinal)
            || configuration.GetValue<bool>("Benchmarks:Enabled")
            || configuration["KeyLoadTests:TimeoutMinutes"] is { } timeout && timeout != TestSuiteProtocol.ProfileTimeoutMinutesText
            || configuration[TestSuiteSettings.ScaleProfileSetting] is not null
            || configuration[ComparisonWorkerSelection.ScaleProfileSetting] is not null
            || configuration[ComparisonWorkerSelection.VectorProfileSetting] is not null
            || configuration["KeyLoadTests:Filter"] != TestSuiteProtocol.IsolatedComparisonFilter
            || configuration[ComparisonWorkerSelection.ScenarioSetting] != nameof(Scenario.VectorExact)
            || !int.TryParse(configuration[ComparisonWorkerSelection.NodeCountSetting], NumberStyles.None,
                CultureInfo.InvariantCulture, out var nodes) || !IsolatedComparisonContract.Current.NodeCounts.Contains(nodes)
            || overrides)
        {
            throw new InvalidOperationException(MessageText);
        }

        try
        {
            var profile = VectorComparisonProfile.Parse(value);
            if (configuration[ComparisonWorkerSelection.ProfileSetting] != profile.Id)
            {
                throw new InvalidOperationException(MessageText);
            }

            return profile;
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException(MessageText);
        }
    }
}
