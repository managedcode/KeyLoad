using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.CodeQuality;

/// <summary>Resolves the native format before an Aspire-owned test process starts.</summary>
[ConfigurationBinding]
internal static class NativeCoverageSelection
{
    internal static string Read(IConfiguration configuration, string suite,
        string? settings, string? output)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var selected = configuration[NativeCoverageProtocol.FormatSetting];
        if (suite == TestSuiteProtocol.ComparisonSuite
            && (settings is not null || output is not null || selected is not null))
        {
            throw new InvalidOperationException(NativeCoverageProtocol.InvalidSelection);
        }
        if (selected is null)
        {
            return NativeCoverageProtocol.CoberturaFormat;
        }
        if (settings is null || output is null
            || selected is not (NativeCoverageProtocol.CoberturaFormat or NativeCoverageProtocol.BinaryFormat)
            || selected == NativeCoverageProtocol.BinaryFormat && !IsFunctionalSuite(suite))
        {
            throw new InvalidOperationException(NativeCoverageProtocol.InvalidSelection);
        }
        return selected;
    }

    private static bool IsFunctionalSuite(string suite) => suite is TestSuiteProtocol.UnitSuite
        or TestSuiteProtocol.ScalarUnitSuite or TestSuiteProtocol.RecoverySuite or TestSuiteProtocol.Rf3Suite;

    internal static void RejectWithoutSuite(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration[NativeCoverageProtocol.FormatSetting] is not null)
        {
            throw new InvalidOperationException(NativeCoverageProtocol.InvalidSelection);
        }
    }
}
